using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using SakugaVault.Extensions;
using SakugaVault.Services.Scraping;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Proxies provider media through the API so browsers can request stable byte ranges
/// and so streams behind a header gate (e.g. a Referer-locked HLS host) play at all.
/// Some upstream MP4 files are not fast-start optimized and stall when the browser's
/// first request is a full-file GET. HLS playlists are rewritten so their key and
/// segment URLs are fetched back through this proxy with the required headers injected.
/// </summary>
public sealed partial class PlaybackStreamProxyService(
    IPlaybackStreamRegistry streamRegistry,
    IPlaybackSessionService playbackSessionService,
    IHttpContextAccessor httpContextAccessor,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<PlaybackStreamProxyService> logger) : IPlaybackStreamProxyService
{
    private static readonly TimeSpan StreamLifetime = TimeSpan.FromMinutes(30);
    private const long InitialRangeEnd = 1_048_575;
    private const string HlsContentType = "application/vnd.apple.mpegurl";

    // Resolver base URL used to retry kwik fetches the .NET TLS fingerprint is
    // blocked from (the AES-key endpoint 403s .NET but accepts Python requests).
    private readonly string? fetchFallbackBase = configuration["Scrapers:StreamFetchFallbackUrl"];

    public string Register(StreamScrapeResult stream)
    {
        if (string.IsNullOrWhiteSpace(stream.StreamUrl))
        {
            throw new ArgumentException("A stream URL is required before registering a playback proxy.", nameof(stream));
        }

        var isHls = string.Equals(stream.PreferredProtocol, "HLS", StringComparison.OrdinalIgnoreCase) ||
                    stream.StreamUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);
        return RegisterUrl(stream.StreamUrl, stream.SourceRequestHeaders, isHls);
    }

    public string RegisterUrl(string url, IReadOnlyDictionary<string, string>? headers = null) =>
        RegisterUrl(url, headers, isHls: false);

    private string RegisterUrl(string url, IReadOnlyDictionary<string, string>? headers, bool isHls)
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
            timeProvider.GetUtcNow(),
            isHls);

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

        // HLS playlists rewrite their key/segment URLs to this same route with a ?u=
        // pointer. Resolve the effective upstream target, constrained to the registered
        // stream's host so this can never be turned into an open relay.
        var target = stream.Url;
        if (request.Query.TryGetValue("u", out var encodedTarget) && encodedTarget.Count > 0)
        {
            if (!TryResolveChildTarget(stream.Url, encodedTarget.ToString(), out target))
            {
                return false;
            }
        }

        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, target);
        foreach (var header in stream.Headers)
        {
            if (string.Equals(header.Key, HeaderNames.Range, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Headers.Range.Count > 0)
        {
            upstreamRequest.Headers.TryAddWithoutValidation(HeaderNames.Range, request.Headers.Range.ToString());
        }
        else if (!stream.IsHls)
        {
            // MP4 fast-start nudge. HLS segments must be fetched whole, so no forced range.
            upstreamRequest.Headers.TryAddWithoutValidation(HeaderNames.Range, $"bytes=0-{InitialRangeEnd}");
        }

        // The kwik CDN origin 403s any segment/key fetch without a browser User-Agent.
        // Force one onto every upstream request, overriding whatever the resolver or a
        // stale cached resolution supplied, so this can never regress.
        upstreamRequest.Headers.Remove("User-Agent");
        upstreamRequest.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

        // The kwik key endpoint also rejects requests that omit the Accept /
        // Accept-Language headers a real browser always sends (.NET HttpClient
        // sends neither by default), so a bare UA+Referer GET 403s on a cache
        // miss while a browser-shaped request succeeds. Add them if absent.
        if (!upstreamRequest.Headers.Contains("Accept"))
        {
            upstreamRequest.Headers.TryAddWithoutValidation("Accept", "*/*");
        }
        if (!upstreamRequest.Headers.Contains("Accept-Language"))
        {
            upstreamRequest.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        }

        var client = httpClientFactory.CreateClient("stream-proxy-client");
        var upstreamResponse = await client.SendAsync(
            upstreamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        try
        {
            // The kwik AES-key endpoint blocks .NET HttpClient's TLS fingerprint
            // (403) even for cached objects, while it accepts the resolver's
            // Python `requests` client. Retry the 403 through the resolver's
            // /fetch proxy so the key (and any similarly gated resource) plays.
            if (upstreamResponse.StatusCode == HttpStatusCode.Forbidden && !string.IsNullOrWhiteSpace(fetchFallbackBase))
            {
                var fallbackUrl = $"{fetchFallbackBase.TrimEnd('/')}/fetch?url={Uri.EscapeDataString(target)}";
                using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, fallbackUrl);
                if (request.Headers.Range.Count > 0)
                {
                    fallbackRequest.Headers.TryAddWithoutValidation(HeaderNames.Range, request.Headers.Range.ToString());
                }

                var fallbackResponse = await client.SendAsync(
                    fallbackRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                logger.LogInformation(
                    "Stream proxy 403 for {Target} retried via resolver fetch -> {Status}",
                    target,
                    (int)fallbackResponse.StatusCode);
                upstreamResponse.Dispose();
                upstreamResponse = fallbackResponse;
            }

            if (!upstreamResponse.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Playback stream proxy request {StreamId} failed with upstream status code {StatusCode} for {Target}",
                    streamId,
                    (int)upstreamResponse.StatusCode,
                    target);
            }

            if (stream.IsHls && IsPlaylist(upstreamResponse, target))
            {
                await WriteRewrittenPlaylistAsync(streamId, target, upstreamResponse, response, cancellationToken);
                return true;
            }

            response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyContentHeader(upstreamResponse, response, HeaderNames.ContentType);
            CopyContentHeader(upstreamResponse, response, HeaderNames.ContentLength);
            CopyContentHeader(upstreamResponse, response, HeaderNames.ContentRange);
            CopyContentHeader(upstreamResponse, response, HeaderNames.LastModified);
            CopyContentHeader(upstreamResponse, response, HeaderNames.ETag);
            response.Headers.AcceptRanges = "bytes";
            // Only cache successful media. A transient upstream error (e.g. a 403 from a
            // cache-miss segment) must NOT be cached by the browser, or it sticks for the
            // whole window and replays even after the origin recovers.
            response.Headers.CacheControl = upstreamResponse.IsSuccessStatusCode
                ? "private, max-age=300"
                : "no-store";

            await upstreamResponse.Content.CopyToAsync(response.Body, cancellationToken);
            return true;
        }
        finally
        {
            upstreamResponse.Dispose();
        }
    }

    private async Task WriteRewrittenPlaylistAsync(
        Guid streamId,
        string playlistUrl,
        HttpResponseMessage upstreamResponse,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var body = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);
        var rewritten = RewritePlaylist(body, new Uri(playlistUrl), streamId);

        response.StatusCode = upstreamResponse.IsSuccessStatusCode ? StatusCodes.Status200OK : (int)upstreamResponse.StatusCode;
        response.ContentType = HlsContentType;
        response.Headers.CacheControl = "private, max-age=10";
        await response.WriteAsync(rewritten, cancellationToken);
    }

    private static string RewritePlaylist(string body, Uri baseUri, Guid streamId)
    {
        var builder = new StringBuilder(body.Length + 256);
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                builder.Append('\n');
                continue;
            }

            if (line[0] == '#')
            {
                // Rewrite URI="..." attributes (EXT-X-KEY, EXT-X-MEDIA, EXT-X-MAP, ...).
                builder.Append(UriAttributeRegex().Replace(line, match =>
                {
                    var proxied = ProxyChildUrl(streamId, baseUri, match.Groups[1].Value);
                    return $"URI=\"{proxied}\"";
                }));
            }
            else
            {
                builder.Append(ProxyChildUrl(streamId, baseUri, line.Trim()));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static string ProxyChildUrl(Guid streamId, Uri baseUri, string reference)
    {
        if (!Uri.TryCreate(baseUri, reference, out var absolute))
        {
            return reference;
        }

        return $"/api/watch/streams/{streamId:D}?u={Base64UrlEncode(absolute.AbsoluteUri)}";
    }

    private static bool TryResolveChildTarget(string registeredUrl, string encodedTarget, out string target)
    {
        target = registeredUrl;
        if (!TryBase64UrlDecode(encodedTarget, out var candidate) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var candidateUri) ||
            (candidateUri.Scheme != Uri.UriSchemeHttp && candidateUri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        // Constrain proxied children to the registered playlist's host.
        if (!string.Equals(candidateUri.Host, new Uri(registeredUrl).Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        target = candidateUri.AbsoluteUri;
        return true;
    }

    private static bool IsPlaylist(HttpResponseMessage upstreamResponse, string url)
    {
        var contentType = upstreamResponse.Content.Headers.ContentType?.MediaType;
        if (contentType is not null &&
            (contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("x-mpegURL", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);
    }

    private static string Base64UrlEncode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryBase64UrlDecode(string value, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
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

    [GeneratedRegex("URI=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex UriAttributeRegex();
}
