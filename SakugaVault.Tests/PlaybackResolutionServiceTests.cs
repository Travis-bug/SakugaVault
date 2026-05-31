using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SakugaVault.Contracts.Watch;
using SakugaVault.Models;
using SakugaVault.Options;
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
}
