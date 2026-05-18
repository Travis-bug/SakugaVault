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

    private sealed class StubStreamScraperService : IStreamScraperService
    {
        public Dictionary<string, StreamScrapeResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> AttemptedProviders { get; } = [];

        public Task<StreamScrapeResult> ResolveStreamAsync(
            Anime anime,
            int episodeNumber,
            string preferredLanguage,
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
}
