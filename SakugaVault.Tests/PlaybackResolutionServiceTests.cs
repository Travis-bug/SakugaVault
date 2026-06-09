using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SakugaVault.Contracts.Watch;
using SakugaVault.Models;
using SakugaVault.Options;
using SakugaVault.Services.Redis;
using SakugaVault.Services.Scraping;
using SakugaVault.Services.Watch;

namespace SakugaVault.Tests;

public sealed class PlaybackResolutionServiceTests
{
    [Fact]
    public async Task ResolveAsync_PrimaryProviderFails_UsesConfiguredFallback()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var anime = new Anime
        {
            Slug = "vinland-saga",
            Title = "Vinland Saga",
            Synopsis = "A story of war and revenge.",
            PosterImageUrl = "https://images.test/vinland-poster.jpg",
            BackdropImageUrl = "https://images.test/vinland-backdrop.jpg",
            EpisodeCount = 24,
            RuntimeMinutes = 24,
            SubAvailable = true,
            DubAvailable = true,
            MetadataProvider = "gogoanime",
            ExternalMetadataId = "vinland-saga"
        };

        testDatabase.DbContext.Anime.Add(anime);
        await testDatabase.DbContext.SaveChangesAsync();

        var streamScraper = new StubStreamScraperService();
        streamScraper.Results["gogoanime"] = new StreamScrapeResult(
            false,
            "HLS",
            null,
            "gogoanime",
            "gogoanime",
            "Primary provider failed.");
        streamScraper.Results["zoro"] = new StreamScrapeResult(
            true,
            "HLS",
            "https://streams.test/vinland.m3u8",
            "zoro",
            "zoro",
            "Playback source resolved successfully.");

        var service = new PlaybackResolutionService(
            testDatabase.DbContext,
            streamScraper,
            new StubPlaybackStreamProxyService(),
            new StubStreamCacheService(),
            new StubScraperRateLimiter(),
            new StubStreamResolutionLockService(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            Microsoft.Extensions.Options.Options.Create(new ScraperOptions
            {
                RequestTimeoutSeconds = 15,
                FallbackProviders = ["zoro", "animefox"]
            }),
            NullLogger<PlaybackResolutionService>.Instance);

        var result = await service.ResolveAsync(
            anime.Id,
            new PlaybackResolutionRequestDto(1, "sub", null),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.True(result.Value!.IsResolved);
        Assert.True(result.Value.UsedFallback);
        Assert.Equal("https://streams.test/vinland.m3u8", result.Value.StreamUrl);
        Assert.Equal(["gogoanime", "zoro"], streamScraper.AttemptedProviders);
    }

    [Fact]
    public async Task ResolveAsync_NonHlsStream_UsesShortLivedProxyUrl()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var anime = new Anime
        {
            Slug = "shadow-realm",
            Title = "Daemons of the Shadow Realm",
            Synopsis = "A village mystery.",
            PosterImageUrl = "https://images.test/shadow-poster.jpg",
            BackdropImageUrl = "https://images.test/shadow-backdrop.jpg",
            EpisodeCount = 8,
            RuntimeMinutes = 24,
            SubAvailable = true,
            DubAvailable = false,
            MetadataProvider = "meta/anilist",
            ExternalMetadataId = "195600"
        };

        testDatabase.DbContext.Anime.Add(anime);
        await testDatabase.DbContext.SaveChangesAsync();

        var streamScraper = new StubStreamScraperService();
        streamScraper.Results["meta/anilist"] = new StreamScrapeResult(
            true,
            "HTTP",
            "https://streams.test/shadow-realm-episode-1.mp4",
            "animesaturn",
            "meta/anilist",
            "Playback source resolved successfully.");
        var proxy = new StubPlaybackStreamProxyService();

        var service = new PlaybackResolutionService(
            testDatabase.DbContext,
            streamScraper,
            proxy,
            new StubStreamCacheService(),
            new StubScraperRateLimiter(),
            new StubStreamResolutionLockService(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            Microsoft.Extensions.Options.Options.Create(new ScraperOptions
            {
                RequestTimeoutSeconds = 15,
                FallbackProviders = ["meta/anilist"]
            }),
            NullLogger<PlaybackResolutionService>.Instance);

        var result = await service.ResolveAsync(
            anime.Id,
            new PlaybackResolutionRequestDto(1, "sub", null),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.True(result.Value!.IsResolved);
        Assert.Equal("/api/watch/streams/test-stream", result.Value.StreamUrl);
        Assert.Equal("https://streams.test/shadow-realm-episode-1.mp4", proxy.RegisteredStream?.StreamUrl);
    }

    [Fact]
    public async Task ResolveAsync_CacheHit_SkipsLiveScraper()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var anime = new Anime
        {
            Slug = "cache-title",
            Title = "Cache Title",
            Synopsis = "Cached playback.",
            PosterImageUrl = "https://images.test/cache-poster.jpg",
            BackdropImageUrl = "https://images.test/cache-backdrop.jpg",
            EpisodeCount = 12,
            RuntimeMinutes = 24,
            SubAvailable = true,
            DubAvailable = false,
            MetadataProvider = "meta/anilist",
            ExternalMetadataId = "100"
        };

        testDatabase.DbContext.Anime.Add(anime);
        await testDatabase.DbContext.SaveChangesAsync();

        var streamScraper = new StubStreamScraperService();
        var cache = new StubStreamCacheService
        {
            CachedResult = new StreamScrapeResult(
                true,
                "HLS",
                "https://streams.test/cached.m3u8",
                "cache",
                "meta/anilist",
                "Cached stream.")
        };

        var service = new PlaybackResolutionService(
            testDatabase.DbContext,
            streamScraper,
            new StubPlaybackStreamProxyService(),
            cache,
            new StubScraperRateLimiter(),
            new StubStreamResolutionLockService(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            Microsoft.Extensions.Options.Options.Create(new ScraperOptions
            {
                RequestTimeoutSeconds = 15,
                FallbackProviders = ["meta/anilist"]
            }),
            NullLogger<PlaybackResolutionService>.Instance);

        var result = await service.ResolveAsync(
            anime.Id,
            new PlaybackResolutionRequestDto(1, "sub", null),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsResolved);
        Assert.Equal("https://streams.test/cached.m3u8", result.Value.StreamUrl);
        Assert.Empty(streamScraper.AttemptedProviders);
    }

    [Fact]
    public async Task ResolveAsync_RateLimitRejected_ReturnsFailureBeforeScrape()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var anime = new Anime
        {
            Slug = "limited-title",
            Title = "Limited Title",
            Synopsis = "Rate limited.",
            PosterImageUrl = "https://images.test/limited-poster.jpg",
            BackdropImageUrl = "https://images.test/limited-backdrop.jpg",
            EpisodeCount = 12,
            RuntimeMinutes = 24,
            SubAvailable = true,
            DubAvailable = false,
            MetadataProvider = "meta/anilist",
            ExternalMetadataId = "101"
        };

        testDatabase.DbContext.Anime.Add(anime);
        await testDatabase.DbContext.SaveChangesAsync();

        var streamScraper = new StubStreamScraperService();
        var limiter = new StubScraperRateLimiter
        {
            Result = new ScraperRateLimitResult(false, 0, TimeSpan.FromMinutes(2))
        };

        var service = new PlaybackResolutionService(
            testDatabase.DbContext,
            streamScraper,
            new StubPlaybackStreamProxyService(),
            new StubStreamCacheService(),
            limiter,
            new StubStreamResolutionLockService(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            Microsoft.Extensions.Options.Options.Create(new ScraperOptions
            {
                RequestTimeoutSeconds = 15,
                FallbackProviders = ["meta/anilist"]
            }),
            NullLogger<PlaybackResolutionService>.Instance);

        var result = await service.ResolveAsync(
            anime.Id,
            new PlaybackResolutionRequestDto(1, "sub", null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("scrape_rate_limited", result.ErrorCode);
        Assert.Equal(TimeSpan.FromMinutes(2), result.RetryAfter);
        Assert.Empty(streamScraper.AttemptedProviders);
    }

    [Fact]
    public async Task ResolveAsync_DuplicateInFlightScrape_DoesNotStartSecondScraper()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var anime = new Anime
        {
            Slug = "in-flight-title",
            Title = "In Flight Title",
            Synopsis = "Duplicate lock.",
            PosterImageUrl = "https://images.test/in-flight-poster.jpg",
            BackdropImageUrl = "https://images.test/in-flight-backdrop.jpg",
            EpisodeCount = 12,
            RuntimeMinutes = 24,
            SubAvailable = true,
            DubAvailable = false,
            MetadataProvider = "meta/anilist",
            ExternalMetadataId = "102"
        };

        testDatabase.DbContext.Anime.Add(anime);
        await testDatabase.DbContext.SaveChangesAsync();

        var streamScraper = new StubStreamScraperService();
        var service = new PlaybackResolutionService(
            testDatabase.DbContext,
            streamScraper,
            new StubPlaybackStreamProxyService(),
            new StubStreamCacheService(),
            new StubScraperRateLimiter(),
            new StubStreamResolutionLockService { AcquireLock = false },
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            Microsoft.Extensions.Options.Options.Create(new ScraperOptions
            {
                RequestTimeoutSeconds = 15,
                StampedeWaitSeconds = 1,
                FallbackProviders = ["meta/anilist"]
            }),
            NullLogger<PlaybackResolutionService>.Instance);

        var result = await service.ResolveAsync(
            anime.Id,
            new PlaybackResolutionRequestDto(1, "sub", null),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsResolved);
        Assert.Equal("cache-wait", result.Value.Resolver);
        Assert.Empty(streamScraper.AttemptedProviders);
    }

    private sealed class StubStreamScraperService : IStreamScraperService
    {
        public Dictionary<string, StreamScrapeResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> AttemptedProviders { get; } = [];

        public Task<StreamScrapeResult> ResolveStreamAsync(
            Anime anime,
            int episodeNumber,
            string preferredLanguage,
            string audioLanguage,
            string subtitleLanguage,
            bool allowRegionalFallback,
            string? providerOverride,
            CancellationToken cancellationToken)
        {
            var provider = providerOverride ?? anime.MetadataProvider ?? "unknown";
            AttemptedProviders.Add(provider);

            if (Results.TryGetValue(provider, out var result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(new StreamScrapeResult(
                false,
                "HLS",
                null,
                provider,
                provider,
                "No result configured."));
        }
    }

    private sealed class StubPlaybackStreamProxyService : IPlaybackStreamProxyService
    {
        public StreamScrapeResult? RegisteredStream { get; private set; }

        public string Register(StreamScrapeResult stream)
        {
            RegisteredStream = stream;
            return "/api/watch/streams/test-stream";
        }

        public string RegisterUrl(string url, IReadOnlyDictionary<string, string>? headers = null)
        {
            return "/api/watch/streams/test-subtitle";
        }

        public Task<bool> ProxyAsync(
            Guid streamId,
            HttpRequest request,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class StubStreamCacheService : IStreamCacheService
    {
        public StreamScrapeResult? CachedResult { get; init; }
        public List<StreamScrapeResult> StoredResults { get; } = [];

        public Task<StreamScrapeResult?> GetAsync(StreamCacheKey key, CancellationToken cancellationToken)
        {
            return Task.FromResult(CachedResult);
        }

        public Task SetAsync(StreamCacheKey key, StreamScrapeResult result, CancellationToken cancellationToken)
        {
            StoredResults.Add(result);
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(Guid animeId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubScraperRateLimiter : IScraperRateLimiter
    {
        public ScraperRateLimitResult Result { get; init; } = new(true, 4, TimeSpan.Zero);

        public Task<ScraperRateLimitResult> CheckAsync(string partitionKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result);
        }
    }

    private sealed class StubStreamResolutionLockService : IStreamResolutionLockService
    {
        public bool AcquireLock { get; init; } = true;

        public Task<IAsyncDisposable?> TryAcquireAsync(StreamCacheKey key, TimeSpan lifetime, CancellationToken cancellationToken)
        {
            return Task.FromResult<IAsyncDisposable?>(AcquireLock ? NoopAsyncDisposable.Instance : null);
        }
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static NoopAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
