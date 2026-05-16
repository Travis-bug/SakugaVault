using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SakugaVault.Models;
using SakugaVault.Options;

namespace SakugaVault.Services.Scraping;

/// <summary>
/// Consumet-backed stream resolver.
/// It converts a title plus episode request into a real playback source without leaking provider-specific
/// response shapes into the rest of the application.
/// </summary>
public sealed class StreamScraperService(
    IHttpClientFactory httpClientFactory,
    IOptions<ScraperOptions> scraperOptionsAccessor,
    ILogger<StreamScraperService> logger) : IStreamScraperService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ScraperOptions scraperOptions = scraperOptionsAccessor.Value;

    public async Task<StreamScrapeResult> ResolveStreamAsync(
        Anime anime,
        int episodeNumber,
        string preferredLanguage,
        string? providerOverride,
        CancellationToken cancellationToken)
    {
        if (!scraperOptions.EnableHostScrapers)
        {
            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: null,
                Provider: providerOverride ?? anime.MetadataProvider ?? "disabled",
                StatusMessage: "Host scrapers are disabled in configuration.");
        }

        if (string.IsNullOrWhiteSpace(anime.ExternalMetadataId))
        {
            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: providerOverride ?? anime.MetadataProvider,
                Provider: providerOverride ?? anime.MetadataProvider ?? "unknown",
                StatusMessage: "This anime does not have an ExternalMetadataId configured, so playback cannot be resolved.");
        }

        var provider = string.IsNullOrWhiteSpace(providerOverride)
            ? anime.MetadataProvider
            : providerOverride;

        if (string.IsNullOrWhiteSpace(provider))
        {
            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: null,
                Provider: "unknown",
                StatusMessage: "No metadata provider was configured for this anime.");
        }

        var client = httpClientFactory.CreateClient("scraper-client");
        var infoRequestUri = $"/anime/{Uri.EscapeDataString(provider)}/info?id={Uri.EscapeDataString(anime.ExternalMetadataId)}";

        logger.LogInformation(
            "Requesting episode list from provider {Provider} for anime {AnimeId}, external ID {ExternalMetadataId}",
            provider,
            anime.Id,
            anime.ExternalMetadataId);

        using var infoResponse = await client.GetAsync(infoRequestUri, cancellationToken);
        if (!infoResponse.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Episode list request failed for anime {AnimeId}, provider {Provider}. Status code: {StatusCode}",
                anime.Id,
                provider,
                (int)infoResponse.StatusCode);

            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: provider,
                Provider: provider,
                StatusMessage: $"Episode lookup failed with status code {(int)infoResponse.StatusCode}.");
        }

        var animeInfo = await infoResponse.Content.ReadFromJsonAsync<ConsumetAnimeInfoResponse>(JsonOptions, cancellationToken);
        if (animeInfo?.Episodes is null || animeInfo.Episodes.Count == 0)
        {
            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: provider,
                Provider: provider,
                StatusMessage: "The provider returned no episode list for this anime.");
        }

        var episode = ResolveEpisode(animeInfo.Episodes, episodeNumber);
        if (episode?.Id is null)
        {
            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: provider,
                Provider: provider,
                StatusMessage: $"Episode {episodeNumber} is out of range for this provider.");
        }

        var watchRequestUri = $"/anime/{Uri.EscapeDataString(provider)}/watch?episodeId={Uri.EscapeDataString(episode.Id)}";
        logger.LogInformation(
            "Resolving stream sources for anime {AnimeId}, provider {Provider}, episode {EpisodeNumber}, episode ID {EpisodeId}",
            anime.Id,
            provider,
            episodeNumber,
            episode.Id);

        using var watchResponse = await client.GetAsync(watchRequestUri, cancellationToken);
        if (!watchResponse.IsSuccessStatusCode)
        {
            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: provider,
                Provider: provider,
                StatusMessage: $"Source lookup failed with status code {(int)watchResponse.StatusCode}.");
        }

        var watchPayload = await watchResponse.Content.ReadFromJsonAsync<ConsumetWatchResponse>(JsonOptions, cancellationToken);
        var source = SelectBestSource(watchPayload?.Sources, preferredLanguage);
        if (source?.Url is null)
        {
            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: provider,
                Provider: provider,
                StatusMessage: "The provider returned no playable sources.");
        }

        logger.LogInformation(
            "Resolved stream source for anime {AnimeId}, provider {Provider}, episode {EpisodeNumber}, host {SourceHost}",
            anime.Id,
            provider,
            episodeNumber,
            source.Server);

        return new StreamScrapeResult(
            IsResolved: true,
            PreferredProtocol: source.IsM3U8 ? "HLS" : "HTTP",
            StreamUrl: source.Url,
            SourceHost: source.Server ?? provider,
            Provider: provider,
            StatusMessage: "Playback source resolved successfully.");
    }

    private static ConsumetEpisodeResponse? ResolveEpisode(IReadOnlyCollection<ConsumetEpisodeResponse> episodes, int episodeNumber)
    {
        var byNumber = episodes.FirstOrDefault(episode => episode.Number.HasValue && (int)Math.Round(episode.Number.Value) == episodeNumber);
        if (byNumber is not null)
        {
            return byNumber;
        }

        return episodeNumber > 0 && episodeNumber <= episodes.Count
            ? episodes.ElementAt(episodeNumber - 1)
            : null;
    }

    private static ConsumetSourceResponse? SelectBestSource(IReadOnlyCollection<ConsumetSourceResponse>? sources, string preferredLanguage)
    {
        _ = preferredLanguage;

        if (sources is null || sources.Count == 0)
        {
            return null;
        }

        return sources
            .OrderByDescending(source => source.IsM3U8)
            .ThenByDescending(source => ParseQuality(source.Quality))
            .FirstOrDefault();
    }

    private static int ParseQuality(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
        {
            return 0;
        }

        var digits = new string(quality.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    private sealed record ConsumetAnimeInfoResponse(
        IReadOnlyCollection<ConsumetEpisodeResponse>? Episodes);

    private sealed record ConsumetEpisodeResponse(
        string? Id,
        double? Number);

    private sealed record ConsumetWatchResponse(
        IReadOnlyCollection<ConsumetSourceResponse>? Sources);

    private sealed record ConsumetSourceResponse(
        string? Url,
        string? Quality,
        bool IsM3U8,
        string? Server);
}
