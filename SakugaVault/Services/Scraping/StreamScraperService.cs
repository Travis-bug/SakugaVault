using System.Net.Http.Json;
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
    IAnimeProviderClient animeProviderClient,
    IHttpClientFactory httpClientFactory,
    IOptions<ScraperOptions> scraperOptionsAccessor,
    ILogger<StreamScraperService> logger) : IStreamScraperService
{
    private readonly ScraperOptions scraperOptions = scraperOptionsAccessor.Value;

    public async Task<StreamScrapeResult> ResolveStreamAsync(
        Anime anime,
        int episodeNumber,
        string preferredLanguage,
        string audioLanguage,
        string subtitleLanguage,
        bool allowRegionalFallback,
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

        var providerInfo = await ResolveProviderInfoAsync(anime, provider, cancellationToken);
        if (providerInfo is null || providerInfo.Episodes.Count == 0)
        {
            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: provider,
                Provider: provider,
                StatusMessage: "The provider returned no episode list for this anime.");
        }

        var episode = ResolveEpisode(providerInfo.Episodes, episodeNumber);
        if (episode is null)
        {
            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: provider,
                Provider: provider,
                StatusMessage: $"Episode {episodeNumber} is out of range for this provider.");
        }

        var watchProvider = string.IsNullOrWhiteSpace(providerInfo.Provider)
            ? provider
            : providerInfo.Provider;

        var client = httpClientFactory.CreateClient("scraper-client");
        var watchRequestUri = BuildWatchRequestUri(
            scraperOptions.ConsumetBaseUrl,
            watchProvider,
            episode.Id,
            anime.Title,
            episodeNumber,
            preferredLanguage,
            audioLanguage,
            subtitleLanguage,
            allowRegionalFallback);
        logger.LogInformation(
            "Resolving stream sources for anime {AnimeId}, requested provider {RequestedProvider}, watch provider {WatchProvider}, episode {EpisodeNumber}, episode ID {EpisodeId}",
            anime.Id,
            provider,
            watchProvider,
            episodeNumber,
            episode.Id);

        using var watchResponse = await client.GetAsync(watchRequestUri, cancellationToken);
        if (!watchResponse.IsSuccessStatusCode)
        {
            var errorPayload = await ReadErrorResponseAsync(watchResponse, cancellationToken);
            logger.LogWarning(
                "Provider {Provider} source lookup for anime {AnimeId}, episode {EpisodeNumber} failed with status code {StatusCode}",
                watchProvider,
                anime.Id,
                episodeNumber,
                (int)watchResponse.StatusCode);

            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: watchProvider,
                Provider: watchProvider,
                StatusMessage: errorPayload?.Message ?? BuildSourceLookupFailureMessage((int)watchResponse.StatusCode));
        }

        var watchPayload = await watchResponse.Content.ReadFromJsonAsync<ConsumetWatchResponse>(cancellationToken);
        var source = SelectBestSource(watchPayload?.Sources, preferredLanguage);
        if (source?.Url is null)
        {
            return new StreamScrapeResult(
                IsResolved: false,
                PreferredProtocol: "HLS",
                StreamUrl: null,
                SourceHost: watchProvider,
                Provider: watchProvider,
                StatusMessage: "The provider returned no playable sources.");
        }

        logger.LogInformation(
            "Resolved stream source for anime {AnimeId}, provider {Provider}, episode {EpisodeNumber}, host {SourceHost}",
            anime.Id,
            watchProvider,
            episodeNumber,
            source.Server);

        var usesHardcodedLanguageSource = string.Equals(watchPayload?.LanguageSource, "hardcoded", StringComparison.OrdinalIgnoreCase);

        return new StreamScrapeResult(
            IsResolved: true,
            PreferredProtocol: source.IsM3U8 ? "HLS" : "HTTP",
            StreamUrl: source.Url,
            SourceHost: source.Server ?? watchProvider,
            Provider: watchProvider,
            StatusMessage: string.IsNullOrWhiteSpace(watchPayload?.LanguageWarning)
                ? "Playback source resolved successfully."
                : $"Playback source resolved successfully. {watchPayload.LanguageWarning}")
        {
            AudioLanguage = usesHardcodedLanguageSource
                ? null
                : NormalizeAudioLanguage(watchPayload?.AudioLanguage ?? audioLanguage, preferredLanguage),
            SubtitleLanguage = usesHardcodedLanguageSource
                ? null
                : NormalizeSubtitleLanguage(watchPayload?.SubtitleLanguage ?? subtitleLanguage),
            LanguageWarning = watchPayload?.LanguageWarning,
            SourceRequestHeaders = watchPayload?.Headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SubtitleTracks = NormalizeSubtitleTracks(watchPayload?.Subtitles)
        };
    }

    private static string BuildWatchRequestUri(
        string consumetBaseUrl,
        string provider,
        string episodeId,
        string animeTitle,
        int episodeNumber,
        string preferredLanguage,
        string audioLanguage,
        string subtitleLanguage,
        bool allowRegionalFallback)
    {
        var trimmedBaseUrl = consumetBaseUrl.TrimEnd('/');
        var trimmedProvider = provider.Trim();
        var escapedEpisodeId = Uri.EscapeDataString(episodeId);
        var escapedTitle = Uri.EscapeDataString(animeTitle);
        var normalizedLanguage = NormalizePreferredLanguage(preferredLanguage);
        var normalizedAudioLanguage = NormalizeAudioLanguage(audioLanguage, normalizedLanguage);
        var normalizedSubtitleLanguage = NormalizeSubtitleLanguage(subtitleLanguage);

        var path = trimmedProvider.StartsWith("meta/", StringComparison.OrdinalIgnoreCase)
            ? $"{trimmedBaseUrl}/{trimmedProvider}/watch"
            : $"{trimmedBaseUrl}/anime/{trimmedProvider}/watch";

        return $"{path}?episodeId={escapedEpisodeId}&preferredLanguage={normalizedLanguage}&audioLanguage={normalizedAudioLanguage}&subtitleLanguage={normalizedSubtitleLanguage}&allowRegionalFallback={allowRegionalFallback.ToString().ToLowerInvariant()}&episodeNumber={episodeNumber}&title={escapedTitle}";
    }

    private static string NormalizePreferredLanguage(string preferredLanguage)
    {
        return string.Equals(preferredLanguage, "dub", StringComparison.OrdinalIgnoreCase)
            ? "dub"
            : "sub";
    }

    private static string NormalizeAudioLanguage(string audioLanguage, string preferredLanguage)
    {
        var normalized = NormalizeLanguageCode(audioLanguage);
        if (normalized is "en" or "ja")
        {
            return normalized;
        }

        return string.Equals(preferredLanguage, "dub", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "ja";
    }

    private static string NormalizeSubtitleLanguage(string subtitleLanguage)
    {
        var normalized = NormalizeLanguageCode(subtitleLanguage);
        return normalized is "en" or "ja" or "off" ? normalized : "en";
    }

    private static string NormalizeLanguageCode(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var baseLanguage = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return baseLanguage switch
        {
            "eng" or "english" => "en",
            "jpn" or "jp" or "japanese" => "ja",
            "none" or "false" or "disabled" => "off",
            _ => baseLanguage
        };
    }

    private static string BuildSourceLookupFailureMessage(int statusCode)
    {
        return statusCode switch
        {
            404 => "This episode is not available from the current provider.",
            429 => "The provider is rate-limiting playback requests right now. Please try again shortly.",
            451 => "This provider cannot serve the episode in the current region or legal context.",
            >= 500 => "The provider is temporarily unavailable. Another provider will be tried if configured.",
            _ => "This provider could not return a playable source right now."
        };
    }

    private static async Task<ConsumetErrorResponse?> ReadErrorResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ConsumetErrorResponse>(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ProviderAnimeInfo?> ResolveProviderInfoAsync(
        Anime anime,
        string provider,
        CancellationToken cancellationToken)
    {
        if (string.Equals(provider, anime.MetadataProvider, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(anime.ExternalMetadataId))
        {
            logger.LogInformation(
                "Requesting episode list from provider {Provider} for anime {AnimeId}, external ID {ExternalMetadataId}",
                provider,
                anime.Id,
                anime.ExternalMetadataId);

            var providerInfo = await animeProviderClient.GetAnimeInfoAsync(provider, anime.ExternalMetadataId, cancellationToken);
            if (providerInfo is not null)
            {
                return providerInfo;
            }
        }

        logger.LogInformation(
            "Searching provider {Provider} by title for anime {AnimeId} because no provider-specific external ID could be used",
            provider,
            anime.Id);

        return await animeProviderClient.FindAnimeInfoByTitleAsync(provider, anime.Title, cancellationToken);
    }

    private static ProviderEpisodeInfo? ResolveEpisode(IReadOnlyCollection<ProviderEpisodeInfo> episodes, int episodeNumber)
    {
        var byNumber = episodes.FirstOrDefault(episode => episode.EpisodeNumber == episodeNumber);
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

    private static IReadOnlyCollection<StreamSubtitleTrack> NormalizeSubtitleTracks(IReadOnlyCollection<ConsumetSubtitleResponse>? subtitles)
    {
        if (subtitles is null || subtitles.Count == 0)
        {
            return [];
        }

        return subtitles
            .Where(subtitle => !string.IsNullOrWhiteSpace(subtitle.Url))
            .Select(subtitle => new StreamSubtitleTrack(
                subtitle.Url!,
                NormalizeSubtitleTrackLanguage(subtitle.Language),
                string.IsNullOrWhiteSpace(subtitle.Label) ? NormalizeSubtitleTrackLanguage(subtitle.Language).ToUpperInvariant() : subtitle.Label!,
                string.IsNullOrWhiteSpace(subtitle.Kind) ? "subtitles" : subtitle.Kind!))
            .ToArray();
    }

    private static string NormalizeSubtitleTrackLanguage(string? language)
    {
        var normalized = NormalizeLanguageCode(language);
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private sealed record ConsumetWatchResponse(
        IReadOnlyDictionary<string, string>? Headers,
        IReadOnlyCollection<ConsumetSourceResponse>? Sources,
        string? PreferredLanguage,
        string? AudioLanguage,
        string? SubtitleLanguage,
        string? LanguageSource,
        string? LanguageWarning,
        IReadOnlyCollection<ConsumetSubtitleResponse>? Subtitles);

    private sealed record ConsumetErrorResponse(
        string? Error,
        string? Message,
        string? Cause);

    private sealed record ConsumetSourceResponse(
        string? Url,
        string? Quality,
        bool IsM3U8,
        string? Server);

    private sealed record ConsumetSubtitleResponse(
        string? Url,
        string? Language,
        string? Label,
        string? Kind);
}
