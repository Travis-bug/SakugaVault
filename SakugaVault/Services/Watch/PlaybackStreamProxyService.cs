using Microsoft.Net.Http.Headers;
using SakugaVault.Extensions;
using SakugaVault.Services.Scraping;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Proxies non-HLS provider media through the API so browsers can request stable byte ranges.
/// Some upstream MP4 files are not fast-start optimized and stall when the browser's first request is a full-file GET.
/// </summary>
public sealed class PlaybackStreamProxyService(
    IPlaybackStreamRegistry streamRegistry,
    IPlaybackSessionService playbackSessionService,
    IHttpContextAccessor httpContextAccessor,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ILogger<PlaybackStreamProxyService> logger) : IPlaybackStreamProxyService
{
    private static readonly TimeSpan StreamLifetime = TimeSpan.FromMinutes(30);
    private const long InitialRangeEnd = 1_048_575;

    public string Register(StreamScrapeResult stream)
    {
        if (string.IsNullOrWhiteSpace(stream.StreamUrl))
        {
            throw new ArgumentException("A stream URL is required before registering a playback proxy.", nameof(stream));
        }

        return RegisterUrl(stream.StreamUrl, stream.SourceRequestHeaders);
    }

    public string RegisterUrl(string url, IReadOnlyDictionary<string, string>? headers = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("A URL is required before registering a playback proxy.", nameof(url));
        }

        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("A playback proxy URL can only be registered during an HTTP request.");
        var userId = httpContext.User.GetUserId()
            ?? throw new InvalidOperationException("A playback proxy URL can only be registered for an authenticated user.");
        var playbackSessionId = playbackSessionService.EnsureSession(httpContext, userId);
        var streamId = Guid.NewGuid();

        var stream = new ProxiedPlaybackStream(
            url,
            headers?.ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            userId,
            playbackSessionId,
            timeProvider.GetUtcNow());

        if (!streamRegistry.TryRegister(streamId, stream, StreamLifetime))
        {
            throw new InvalidOperationException("Playback stream registry is unavailable.");
        }

        return $"/api/watch/streams/{streamId:D}";
    }

    public async Task<bool> ProxyAsync(
        Guid streamId,
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!streamRegistry.TryGet(streamId, out var stream) || stream is null)
        {
            return false;
        }

        if (!playbackSessionService.IsAuthorized(request.HttpContext, stream))
        {
            return false;
        }

        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, stream.Url);
        foreach (var header in stream.Headers)
        {
            if (string.Equals(header.Key, HeaderNames.Range, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        upstreamRequest.Headers.TryAddWithoutValidation(
            HeaderNames.Range,
            request.Headers.Range.Count > 0
                ? request.Headers.Range.ToString()
                : $"bytes=0-{InitialRangeEnd}");

        var client = httpClientFactory.CreateClient("stream-proxy-client");
        using var upstreamResponse = await client.SendAsync(
            upstreamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.StatusCode = (int)upstreamResponse.StatusCode;
        CopyContentHeader(upstreamResponse, response, HeaderNames.ContentType);
        CopyContentHeader(upstreamResponse, response, HeaderNames.ContentLength);
        CopyContentHeader(upstreamResponse, response, HeaderNames.ContentRange);
        CopyContentHeader(upstreamResponse, response, HeaderNames.LastModified);
        CopyContentHeader(upstreamResponse, response, HeaderNames.ETag);
        response.Headers.AcceptRanges = "bytes";
        response.Headers.CacheControl = "private, max-age=300";

        if (!upstreamResponse.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Playback stream proxy request {StreamId} failed with upstream status code {StatusCode}",
                streamId,
                (int)upstreamResponse.StatusCode);
        }

        await upstreamResponse.Content.CopyToAsync(response.Body, cancellationToken);
        return true;
    }

    private static void CopyContentHeader(HttpResponseMessage upstreamResponse, HttpResponse response, string headerName)
    {
        if (upstreamResponse.Content.Headers.TryGetValues(headerName, out var contentValues))
        {
            response.Headers[headerName] = contentValues.ToArray();
            return;
        }

        if (upstreamResponse.Headers.TryGetValues(headerName, out var responseValues))
        {
            response.Headers[headerName] = responseValues.ToArray();
        }
    }

}
