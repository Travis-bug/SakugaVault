using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SakugaVault.Services.Watch;

namespace SakugaVault.Tests;

public sealed class PlaybackStreamProxyServiceTests
{
    [Fact]
    public async Task ProxyAsync_MissingPlaybackCookie_ReturnsFalseWithoutCallingUpstream()
    {
        var streamId = Guid.NewGuid();
        var registry = new StubPlaybackStreamRegistry();
        registry.Streams[streamId] = new ProxiedPlaybackStream(
            "https://streams.test/video.mp4",
            [],
            Guid.NewGuid(),
            "matching-session",
            DateTimeOffset.UtcNow);
        var handler = new StubHttpMessageHandler();
        var service = new PlaybackStreamProxyService(
            registry,
            new CookieCheckingPlaybackSessionService(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new StubHttpClientFactory(handler),
            TimeProvider.System,
            NullLogger<PlaybackStreamProxyService>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var proxied = await service.ProxyAsync(streamId, context.Request, context.Response, CancellationToken.None);

        Assert.False(proxied);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ProxyAsync_MatchingPlaybackCookie_StreamsUpstream()
    {
        var streamId = Guid.NewGuid();
        var registry = new StubPlaybackStreamRegistry();
        registry.Streams[streamId] = new ProxiedPlaybackStream(
            "https://streams.test/video.mp4",
            [],
            Guid.NewGuid(),
            "matching-session",
            DateTimeOffset.UtcNow);
        var handler = new StubHttpMessageHandler();
        var service = new PlaybackStreamProxyService(
            registry,
            new CookieCheckingPlaybackSessionService(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new StubHttpClientFactory(handler),
            TimeProvider.System,
            NullLogger<PlaybackStreamProxyService>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{PlaybackSessionConstants.CookieName}=matching-session";
        context.Response.Body = new MemoryStream();

        var proxied = await service.ProxyAsync(streamId, context.Request, context.Response, CancellationToken.None);

        Assert.True(proxied);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(StatusCodes.Status206PartialContent, context.Response.StatusCode);
    }

    private sealed class StubPlaybackStreamRegistry : IPlaybackStreamRegistry
    {
        public Dictionary<Guid, ProxiedPlaybackStream> Streams { get; } = [];

        public bool TryRegister(Guid streamId, ProxiedPlaybackStream stream, TimeSpan ttl)
        {
            Streams[streamId] = stream;
            return true;
        }

        public bool TryGet(Guid streamId, out ProxiedPlaybackStream? stream)
        {
            return Streams.TryGetValue(streamId, out stream);
        }
    }

    private sealed class CookieCheckingPlaybackSessionService : IPlaybackSessionService
    {
        public string EnsureSession(HttpContext context, Guid userId)
        {
            throw new NotImplementedException();
        }

        public bool IsAuthorized(HttpContext context, ProxiedPlaybackStream stream)
        {
            return string.Equals(
                context.Request.Cookies[PlaybackSessionConstants.CookieName],
                stream.PlaybackSessionId,
                StringComparison.Ordinal);
        }

        public void RevokeCurrentSession(HttpContext context)
        {
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([1, 2, 3])
            };
            response.Content.Headers.ContentType = new("video/mp4");
            response.Content.Headers.ContentLength = 3;
            response.Content.Headers.ContentRange = new(0, 2, 3);
            return Task.FromResult(response);
        }
    }
}
