using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SakugaVault.Models;
using SakugaVault.Options;

namespace SakugaVault.Services.Scraping;

/// <summary>
/// Fans playback requests out to independently hosted Consumet-compatible
/// resolvers and selects the strongest reachable language match.
/// </summary>
public sealed class StreamScraperService(
    IHttpClientFactory httpClientFactory,
    IOptions<ScraperOptions> scraperOptionsAccessor,
    ILogger<StreamScraperService> logger) : IStreamScraperService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
        var provider = string.IsNullOrWhiteSpace(providerOverride)
            ? anime.MetadataProvider
            : providerOverride;

        if (!scraperOptions.EnableHostScrapers)
        {
            return Failure(provider ?? "disabled", "disabled", "Host scrapers are disabled in configuration.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            return Failure("unknown", "none", "No metadata provider was configured for this anime.");
        }

        var resolvers = BuildResolverSequence();
        if (resolvers.Count == 0)
        {
            return Failure(provider, "none", "No playback resolvers are enabled in configuration.");
        }

        logger.LogInformation(
            "Resolving anime {AnimeId}, episode {EpisodeNumber} across {ResolverCount} playback resolvers",
            anime.Id,
            episodeNumber,
            resolvers.Count);

        var attempts = await Task.WhenAll(
            resolvers.Select(resolver => ResolveFromEndpointAsync(
                resolver,
                anime,
                provider,
                episodeNumber,
                preferredLanguage,
                audioLanguage,
                subtitleLanguage,
                allowRegionalFallback,
                cancellationToken)));
        var candidates = attempts.SelectMany(attempt => attempt.Candidates).ToArray();
        var best = PlaybackCandidateRanker.SelectBest(candidates, audioLanguage, subtitleLanguage);
        if (best is not null)
        {
            logger.LogInformation(
                "Selected resolver {Resolver} provider {Provider} host {SourceHost} for anime {AnimeId}, episode {EpisodeNumber}",
                best.Resolver,
                best.Provider,
                best.SourceHost,
                anime.Id,
                episodeNumber);
            return best;
        }

        var reasons = attempts
            .Select(attempt => $"{attempt.Resolver}: {attempt.StatusMessage}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4);
        return Failure(
            provider,
            "orchestrator",
            $"No configured scraper returned a playable stream. {string.Join(" ", reasons)}");
    }

    private async Task<ResolverAttempt> ResolveFromEndpointAsync(
        ScraperResolverOptions resolver,
        Anime anime,
        string provider,
        int episodeNumber,
        string preferredLanguage,
        string audioLanguage,
        string subtitleLanguage,
        bool allowRegionalFallback,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(
            resolver.RequestTimeoutSeconds > 0
                ? resolver.RequestTimeoutSeconds
                : scraperOptions.RequestTimeoutSeconds));

        try
        {
            var providerInfo = await ResolveProviderInfoAsync(resolver, anime, provider, timeoutCts.Token);
            if (providerInfo is null || providerInfo.Episodes.Count == 0)
            {
                return ResolverAttempt.Failed(resolver.Name, "The resolver returned no episode list for this anime.");
            }

            var episode = ResolveEpisode(providerInfo.Episodes, episodeNumber);
            if (episode is null)
            {
                return ResolverAttempt.Failed(resolver.Name, $"Episode {episodeNumber} is out of range for this resolver.");
            }

            var watchProvider = string.IsNullOrWhiteSpace(providerInfo.SourceProvider)
                ? provider
                : providerInfo.SourceProvider;
            var requestUri = BuildWatchRequestUri(
                resolver.BaseUrl,
                watchProvider,
                episode.Id,
                anime.Title,
                episodeNumber,
                preferredLanguage,
                audioLanguage,
                subtitleLanguage,
                allowRegionalFallback);
            using var watchResponse = await SendAsync(resolver, requestUri, timeoutCts.Token);
            if (!watchResponse.IsSuccessStatusCode)
            {
                var errorPayload = await ReadJsonAsync<ConsumetErrorResponse>(watchResponse, timeoutCts.Token);
                return ResolverAttempt.Failed(
                    resolver.Name,
                    errorPayload?.Message ?? BuildSourceLookupFailureMessage((int)watchResponse.StatusCode));
            }

            var watchPayload = await ReadJsonAsync<ConsumetWatchResponse>(watchResponse, timeoutCts.Token);
            if (watchPayload?.Sources is null || watchPayload.Sources.Count == 0)
            {
                return ResolverAttempt.Failed(resolver.Name, "The resolver returned no stream URLs.");
            }

            var usesHardcodedLanguageSource = string.Equals(
                watchPayload.LanguageSource,
                "hardcoded",
                StringComparison.OrdinalIgnoreCase);
            if (usesHardcodedLanguageSource && !allowRegionalFallback)
            {
                return ResolverAttempt.Failed(resolver.Name, "The resolver returned an unverified hardcoded subtitle stream.");
            }

            var sourceRequestHeaders = watchPayload.Headers ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var subtitles = NormalizeSubtitleTracks(watchPayload.Subtitles);
            if (!HasRequestedSubtitle(subtitles, watchPayload.SubtitleLanguage, subtitleLanguage))
            {
                return ResolverAttempt.Failed(resolver.Name, $"The resolver did not return {subtitleLanguage} subtitles.");
            }

            var source = await SelectBestReachableSourceAsync(
                watchPayload.Sources,
                sourceRequestHeaders,
                timeoutCts.Token);
            if (source?.Url is null)
            {
                return ResolverAttempt.Failed(resolver.Name, "The resolver returned stream URLs, but none were reachable.");
            }

            var statusMessage = string.IsNullOrWhiteSpace(watchPayload.LanguageWarning)
                ? $"Playback source resolved successfully through {resolver.Name}."
                : $"Playback source resolved successfully through {resolver.Name}. {watchPayload.LanguageWarning}";
            var result = new StreamScrapeResult(
                IsResolved: true,
                PreferredProtocol: source.IsM3U8 ? "HLS" : "HTTP",
                StreamUrl: source.Url,
                SourceHost: source.Server ?? watchProvider,
                Provider: watchProvider,
                StatusMessage: statusMessage)
            {
                Resolver = resolver.Name,
                ResolverPriority = resolver.Priority,
                AudioLanguage = usesHardcodedLanguageSource
                    ? null
                    : NormalizeAudioLanguage(watchPayload.AudioLanguage ?? audioLanguage, preferredLanguage),
                SubtitleLanguage = usesHardcodedLanguageSource
                    ? null
                    : NormalizeSubtitleLanguage(watchPayload.SubtitleLanguage ?? subtitleLanguage),
                LanguageWarning = watchPayload.LanguageWarning,
                SourceRequestHeaders = sourceRequestHeaders,
                SubtitleTracks = subtitles
            };

            logger.LogInformation(
                "Resolver {Resolver} produced candidate provider {Provider} host {SourceHost} in {ElapsedMilliseconds}ms",
                resolver.Name,
                watchProvider,
                result.SourceHost,
                stopwatch.ElapsedMilliseconds);
            return ResolverAttempt.Succeeded(resolver.Name, result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ResolverAttempt.Failed(resolver.Name, "The resolver timed out.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Resolver {Resolver} failed for anime {AnimeId}, episode {EpisodeNumber}",
                resolver.Name,
                anime.Id,
                episodeNumber);
            return ResolverAttempt.Failed(resolver.Name, exception.Message);
        }
    }

    private async Task<EndpointAnimeInfo?> ResolveProviderInfoAsync(
        ScraperResolverOptions resolver,
        Anime anime,
        string provider,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(anime.ExternalMetadataId))
        {
            var direct = await GetAnimeInfoAsync(resolver, provider, anime.ExternalMetadataId, cancellationToken);
            if (direct is not null)
            {
                return direct;
            }
        }

        var searchUri = $"{resolver.BaseUrl.TrimEnd('/')}/meta/anilist/{Uri.EscapeDataString(anime.Title)}";
        using var searchResponse = await SendAsync(resolver, searchUri, cancellationToken);
        if (!searchResponse.IsSuccessStatusCode)
        {
            return null;
        }

        var searchPayload = await ReadJsonAsync<ResolverSearchResponse>(searchResponse, cancellationToken);
        var match = SelectBestMatch(searchPayload?.Results ?? [], anime.Title);
        return string.IsNullOrWhiteSpace(match?.Id)
            ? null
            : await GetAnimeInfoAsync(resolver, provider, match.Id, cancellationToken);
    }

    private async Task<EndpointAnimeInfo?> GetAnimeInfoAsync(
        ScraperResolverOptions resolver,
        string provider,
        string externalId,
        CancellationToken cancellationToken)
    {
        var infoUri = $"{resolver.BaseUrl.TrimEnd('/')}/meta/anilist/info/{Uri.EscapeDataString(externalId)}";
        using var response = await SendAsync(resolver, infoUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await ReadJsonAsync<ResolverInfoResponse>(response, cancellationToken);
        var episodes = payload?.Episodes?
            .Where(episode => !string.IsNullOrWhiteSpace(episode.Id) && episode.Number > 0)
            .Select(episode => new ProviderEpisodeInfo(
                episode.Id!,
                Math.Max(1, (int)Math.Round(episode.Number)),
                string.IsNullOrWhiteSpace(episode.Title)
                    ? $"Episode {episode.Number}"
                    : $"Episode {episode.Number} · {episode.Title}"))
            .DistinctBy(episode => episode.EpisodeNumber)
            .ToArray() ?? [];
        return new EndpointAnimeInfo(payload?.SourceProvider, episodes);
    }

    private async Task<HttpResponseMessage> SendAsync(
        ScraperResolverOptions resolver,
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        foreach (var header in resolver.RequestHeaders ?? [])
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return await httpClientFactory
            .CreateClient("scraper-client")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<ConsumetSourceResponse?> SelectBestReachableSourceAsync(
        IReadOnlyCollection<ConsumetSourceResponse> sources,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        foreach (var source in sources
                     .Where(source => !string.IsNullOrWhiteSpace(source.Url))
                     .OrderByDescending(source => source.IsM3U8)
                     .ThenByDescending(source => ParseQuality(source.Quality)))
        {
            if (await IsReachableAsync(source.Url!, headers, cancellationToken))
            {
                return source;
            }
        }

        return null;
    }

    private async Task<bool> IsReachableAsync(
        string url,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        try
        {
            using var response = await httpClientFactory
                .CreateClient("stream-proxy-client")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            return response.IsSuccessStatusCode
                || response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            logger.LogInformation(exception, "Rejected unreachable playback source {StreamUrl}", url);
            return false;
        }
    }

    private IReadOnlyList<ScraperResolverOptions> BuildResolverSequence()
    {
        var configured = (scraperOptions.PlaybackResolvers ?? [])
            .Where(resolver => resolver.Enabled)
            .Where(resolver => !string.IsNullOrWhiteSpace(resolver.Name))
            .Where(resolver => Uri.TryCreate(resolver.BaseUrl, UriKind.Absolute, out _))
            .OrderBy(resolver => resolver.Priority)
            .ToArray();

        return configured.Length > 0
            ? configured
            : [
                new ScraperResolverOptions
                {
                    Name = "consumet-compatible",
                    BaseUrl = scraperOptions.ConsumetBaseUrl,
                    Priority = 100
                }
            ];
    }

    private static ProviderEpisodeInfo? ResolveEpisode(IReadOnlyCollection<ProviderEpisodeInfo> episodes, int episodeNumber)
    {
        return episodes.FirstOrDefault(episode => episode.EpisodeNumber == episodeNumber) ??
               (episodeNumber > 0 && episodeNumber <= episodes.Count
                   ? episodes.ElementAt(episodeNumber - 1)
                   : null);
    }

    private static ResolverSearchTitle? SelectBestMatch(
        IReadOnlyCollection<ResolverSearchTitle> results,
        string title)
    {
        var normalizedTitle = NormalizeTitle(title);
        return results
            .Where(result => !string.IsNullOrWhiteSpace(result.Id))
            .OrderByDescending(result => NormalizeTitle(result.Title) == normalizedTitle)
            .ThenBy(result => Math.Abs(NormalizeTitle(result.Title).Length - normalizedTitle.Length))
            .FirstOrDefault();
    }

    private static string NormalizeTitle(string? title)
    {
        return string.Concat((title ?? string.Empty)
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character)))
            .Trim();
    }

    private static bool HasRequestedSubtitle(
        IReadOnlyCollection<StreamSubtitleTrack> subtitles,
        string? resolvedSubtitleLanguage,
        string requestedSubtitleLanguage)
    {
        var requested = NormalizeSubtitleLanguage(requestedSubtitleLanguage);
        if (requested == "off" || subtitles.Count == 0)
        {
            return true;
        }

        return subtitles.Any(subtitle =>
            string.Equals(
                NormalizeSubtitleLanguage(subtitle.Language),
                requested,
                StringComparison.OrdinalIgnoreCase)) ||
               string.Equals(
                   NormalizeSubtitleLanguage(resolvedSubtitleLanguage ?? string.Empty),
                   requested,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildWatchRequestUri(
        string baseUrl,
        string provider,
        string episodeId,
        string animeTitle,
        int episodeNumber,
        string preferredLanguage,
        string audioLanguage,
        string subtitleLanguage,
        bool allowRegionalFallback)
    {
        var path = provider.StartsWith("meta/", StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl.TrimEnd('/')}/{provider.Trim('/')}/watch"
            : $"{baseUrl.TrimEnd('/')}/anime/{provider.Trim('/')}/watch";
        return $"{path}?episodeId={Uri.EscapeDataString(episodeId)}&preferredLanguage={NormalizePreferredLanguage(preferredLanguage)}&audioLanguage={NormalizeAudioLanguage(audioLanguage, preferredLanguage)}&subtitleLanguage={NormalizeSubtitleLanguage(subtitleLanguage)}&allowRegionalFallback={allowRegionalFallback.ToString().ToLowerInvariant()}&episodeNumber={episodeNumber}&title={Uri.EscapeDataString(animeTitle)}";
    }

    private static string NormalizePreferredLanguage(string preferredLanguage)
    {
        return string.Equals(preferredLanguage, "dub", StringComparison.OrdinalIgnoreCase) ? "dub" : "sub";
    }

    private static string NormalizeAudioLanguage(string audioLanguage, string preferredLanguage)
    {
        var normalized = NormalizeLanguageCode(audioLanguage);
        return normalized is "en" or "ja"
            ? normalized
            : string.Equals(preferredLanguage, "dub", StringComparison.OrdinalIgnoreCase) ? "en" : "ja";
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

        return normalized.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0] switch
        {
            "eng" or "english" => "en",
            "jpn" or "jp" or "japanese" => "ja",
            "none" or "false" or "disabled" => "off",
            var value => value
        };
    }

    private static IReadOnlyCollection<StreamSubtitleTrack> NormalizeSubtitleTracks(
        IReadOnlyCollection<ConsumetSubtitleResponse>? subtitles)
    {
        return subtitles?
            .Where(subtitle => !string.IsNullOrWhiteSpace(subtitle.Url))
            .Select(subtitle => new StreamSubtitleTrack(
                subtitle.Url!,
                string.IsNullOrWhiteSpace(subtitle.Language) ? "unknown" : subtitle.Language!,
                string.IsNullOrWhiteSpace(subtitle.Label) ? "Subtitle" : subtitle.Label!,
                string.IsNullOrWhiteSpace(subtitle.Kind) ? "subtitles" : subtitle.Kind!))
            .ToArray() ?? [];
    }

    private static int ParseQuality(string? quality)
    {
        var digits = new string((quality ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string BuildSourceLookupFailureMessage(int statusCode)
    {
        return statusCode switch
        {
            404 => "This episode is not available from the current resolver.",
            429 => "The resolver is rate-limiting playback requests right now.",
            451 => "The resolver cannot serve the episode in the current region.",
            >= 500 => "The resolver is temporarily unavailable.",
            _ => "The resolver could not return a playable source right now."
        };
    }

    private static StreamScrapeResult Failure(string provider, string resolver, string message)
    {
        return new StreamScrapeResult(false, "HLS", null, null, provider, message)
        {
            Resolver = resolver
        };
    }

    private sealed record ResolverAttempt(
        string Resolver,
        IReadOnlyCollection<StreamScrapeResult> Candidates,
        string StatusMessage)
    {
        public static ResolverAttempt Succeeded(string resolver, StreamScrapeResult result) =>
            new(resolver, [result], result.StatusMessage);

        public static ResolverAttempt Failed(string resolver, string statusMessage) =>
            new(resolver, [], statusMessage);
    }

    private sealed record EndpointAnimeInfo(
        string? SourceProvider,
        IReadOnlyCollection<ProviderEpisodeInfo> Episodes);

    private sealed record ResolverInfoResponse(
        string? SourceProvider,
        IReadOnlyCollection<ResolverEpisodeResponse>? Episodes);

    private sealed record ResolverEpisodeResponse(
        string? Id,
        double Number,
        string? Title);

    private sealed record ResolverSearchResponse(
        IReadOnlyCollection<ResolverSearchTitle>? Results);

    private sealed record ResolverSearchTitle(
        string? Id,
        string? Title);

    private sealed record ConsumetWatchResponse(
        IReadOnlyDictionary<string, string>? Headers,
        IReadOnlyCollection<ConsumetSourceResponse>? Sources,
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
