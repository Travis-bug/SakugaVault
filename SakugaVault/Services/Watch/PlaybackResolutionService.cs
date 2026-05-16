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

        return OperationResult<ResolvedPlaybackDto>.Success(
            new ResolvedPlaybackDto(
                anime.Id,
                request.EpisodeNumber,
                finalResult.IsResolved,
                finalResult.PreferredProtocol,
                finalResult.StreamUrl,
                finalResult.SourceHost,
                usedFallback,
                finalResult.StatusMessage));
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
