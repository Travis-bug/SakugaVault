using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SakugaVault.Contracts.Watch;
using SakugaVault.Data;
using SakugaVault.Options;
using SakugaVault.Services.Common;
using SakugaVault.Services.Scraping;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Orchestrates playback resolution by loading title metadata and delegating stream lookup to the scraper layer.
/// This service now owns provider override and fallback-provider policy so controllers stay unaware of host sequencing rules.
/// </summary>
public sealed class PlaybackResolutionService(
    SakugaVaultDbContext dbContext,
    IStreamScraperService streamScraperService,
    IPlaybackStreamProxyService playbackStreamProxyService,
    IOptions<ScraperOptions> scraperOptionsAccessor,
    ILogger<PlaybackResolutionService> logger) : IPlaybackResolutionService
{
    private readonly ScraperOptions scraperOptions = scraperOptionsAccessor.Value;

    public async Task<OperationResult<ResolvedPlaybackDto>> ResolveAsync(Guid animeId, PlaybackResolutionRequestDto request, CancellationToken cancellationToken)
    {
        var anime = await dbContext.Anime
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == animeId, cancellationToken);

        if (anime is null)
        {
            return OperationResult<ResolvedPlaybackDto>.Failure("anime_not_found", "The requested anime could not be found.");
        }

        var attemptedProviders = BuildProviderSequence(anime.MetadataProvider, request.ProviderOverride);
        StreamScrapeResult? finalResult = null;
        var usedFallback = false;

        for (var index = 0; index < attemptedProviders.Count; index++)
        {
            var provider = attemptedProviders[index];
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(scraperOptions.RequestTimeoutSeconds));

            try
            {
                logger.LogInformation(
                    "Attempting playback resolution for anime {AnimeId}, episode {EpisodeNumber}, provider {Provider}",
                    anime.Id,
                    request.EpisodeNumber,
                    provider);

                finalResult = await streamScraperService.ResolveStreamAsync(
                    anime,
                    request.EpisodeNumber,
                    request.PreferredLanguage,
                    request.AudioLanguage,
                    request.SubtitleLanguage,
                    request.AllowRegionalFallback,
                    provider,
                    timeoutCts.Token);

                if (finalResult.IsResolved)
                {
                    usedFallback = index > 0;
                    break;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Skipping provider {Provider} for anime {AnimeId} after hitting the per-provider timeout of {TimeoutSeconds}s",
                    provider,
                    anime.Id,
                    scraperOptions.RequestTimeoutSeconds);

                finalResult = new StreamScrapeResult(
                    IsResolved: false,
                    PreferredProtocol: "HLS",
                    StreamUrl: null,
                    SourceHost: provider,
                    Provider: provider,
                    StatusMessage: $"Provider {provider} timed out after {scraperOptions.RequestTimeoutSeconds} seconds.");
            }
        }

        finalResult ??= new StreamScrapeResult(
            IsResolved: false,
            PreferredProtocol: "HLS",
            StreamUrl: null,
            SourceHost: null,
            Provider: request.ProviderOverride ?? anime.MetadataProvider ?? "unknown",
            StatusMessage: "No playback providers were configured for this title.");

        if (!finalResult.IsResolved && string.IsNullOrWhiteSpace(finalResult.StatusMessage))
        {
            finalResult = finalResult with
            {
                StatusMessage = "This episode is temporarily unavailable from the current providers. Please try again later."
            };
        }

        var streamUrl = finalResult.StreamUrl;
        if (ShouldProxyStream(finalResult))
        {
            streamUrl = playbackStreamProxyService.Register(finalResult);
        }

        var usesUnverifiedLanguageSource = !string.IsNullOrWhiteSpace(finalResult.LanguageWarning);
        var subtitles = finalResult.SubtitleTracks
            .Select(track => new PlaybackSubtitleDto(
                playbackStreamProxyService.RegisterUrl(track.Url, finalResult.SourceRequestHeaders),
                track.Language,
                track.Label,
                track.Kind))
            .ToArray();

        return OperationResult<ResolvedPlaybackDto>.Success(
            new ResolvedPlaybackDto(
                anime.Id,
                request.EpisodeNumber,
                finalResult.IsResolved,
                finalResult.PreferredProtocol,
                streamUrl,
                finalResult.SourceHost,
                usesUnverifiedLanguageSource
                    ? null
                    : finalResult.AudioLanguage ?? NormalizeAudioLanguage(request.AudioLanguage, request.PreferredLanguage),
                usesUnverifiedLanguageSource
                    ? null
                    : finalResult.SubtitleLanguage ?? NormalizeSubtitleLanguage(request.SubtitleLanguage),
                usedFallback,
                subtitles,
                finalResult.StatusMessage));
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

    private static bool ShouldProxyStream(StreamScrapeResult result)
    {
        if (!result.IsResolved || string.IsNullOrWhiteSpace(result.StreamUrl))
        {
            return false;
        }

        return !string.Equals(result.PreferredProtocol, "HLS", StringComparison.OrdinalIgnoreCase) &&
               !result.StreamUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> BuildProviderSequence(string? metadataProvider, string? providerOverride)
    {
        var providers = new List<string>();

        if (!string.IsNullOrWhiteSpace(providerOverride))
        {
            providers.Add(providerOverride.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(metadataProvider))
        {
            providers.Add(metadataProvider.Trim());
        }

        foreach (var fallbackProvider in scraperOptions.FallbackProviders)
        {
            if (string.IsNullOrWhiteSpace(fallbackProvider))
            {
                continue;
            }

            if (providers.Contains(fallbackProvider, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            providers.Add(fallbackProvider.Trim());
        }

        return providers;
    }
}
