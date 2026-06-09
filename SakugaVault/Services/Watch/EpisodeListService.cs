using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SakugaVault.Contracts.Watch;
using SakugaVault.Data;
using SakugaVault.Options;
using SakugaVault.Services.Scraping;
using StackExchange.Redis;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Resolves provider-backed episode lists after the watch page has already rendered.
/// The lookup fans out across providers with short per-provider timeouts and caches successful results in Redis.
/// </summary>
public sealed class EpisodeListService(
    SakugaVaultDbContext dbContext,
    IAnimeProviderClient animeProviderClient,
    IConnectionMultiplexer redis,
    IOptions<ScraperOptions> scraperOptionsAccessor,
    ILogger<EpisodeListService> logger) : IEpisodeListService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int PerProviderTimeoutMilliseconds = 5_000;

    public async Task<EpisodeListResult> GetEpisodesAsync(Guid animeId, CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(animeId);
        var cached = await TryGetCachedResultAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            logger.LogDebug("Episode list cache hit for anime {AnimeId}.", animeId);
            return cached;
        }

        var anime = await dbContext.Anime
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == animeId, cancellationToken);

        if (anime is null)
        {
            return new EpisodeListResult(false, string.Empty, [], "Anime not found.");
        }

        var providers = BuildProviderSequence(anime.MetadataProvider, scraperOptionsAccessor.Value);
        if (providers.Count == 0)
        {
            return new EpisodeListResult(false, anime.MetadataProvider ?? "unknown", [], "No providers are configured for this title.");
        }

        var attempts = await Task.WhenAll(providers.Select(provider =>
            FetchProviderEpisodesAsync(anime, provider, cancellationToken)));
        var winner = attempts.FirstOrDefault(attempt => attempt.Episodes is { Count: > 0 });

        if (winner is null || winner.Episodes is null || winner.Episodes.Count == 0)
        {
            var summary = string.Join("; ", attempts.Select(attempt => $"{attempt.Provider}: {attempt.Error ?? "no episodes"}"));
            logger.LogWarning("All providers failed to return episodes for anime {AnimeId}. {Summary}", animeId, summary);
            return new EpisodeListResult(
                false,
                anime.MetadataProvider ?? "unknown",
                [],
                "Episode list is temporarily unavailable. Try again shortly.");
        }

        var result = new EpisodeListResult(true, winner.Provider, BuildSeasons(winner.Episodes), null);
        await TrySetCachedResultAsync(cacheKey, result, animeId, cancellationToken);
        return result;
    }

    private async Task<ProviderEpisodeAttempt> FetchProviderEpisodesAsync(
        Models.Anime anime,
        string provider,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PerProviderTimeoutMilliseconds);

        try
        {
            ProviderAnimeInfo? providerInfo;
            if (string.Equals(provider, anime.MetadataProvider, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(anime.ExternalMetadataId))
            {
                providerInfo = await animeProviderClient.GetAnimeInfoAsync(provider, anime.ExternalMetadataId, timeoutCts.Token);
            }
            else
            {
                providerInfo = await animeProviderClient.FindAnimeInfoByTitleAsync(provider, anime.Title, timeoutCts.Token);
            }

            return providerInfo?.Episodes is { Count: > 0 } episodes
                ? new ProviderEpisodeAttempt(provider, episodes, null)
                : new ProviderEpisodeAttempt(provider, null, "provider returned no episodes");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Provider {Provider} timed out after {TimeoutMilliseconds}ms while fetching episodes for anime {AnimeId}.",
                provider,
                PerProviderTimeoutMilliseconds,
                anime.Id);
            return new ProviderEpisodeAttempt(provider, null, $"timed out after {PerProviderTimeoutMilliseconds}ms");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Provider {Provider} failed while fetching episodes for anime {AnimeId}.", provider, anime.Id);
            return new ProviderEpisodeAttempt(provider, null, exception.Message);
        }
    }

    private static IReadOnlyCollection<WatchSeasonDto> BuildSeasons(IReadOnlyCollection<ProviderEpisodeInfo> episodes)
    {
        var ordered = episodes
            .OrderBy(episode => episode.EpisodeNumber)
            .Select(episode => new WatchEpisodeDto(episode.EpisodeNumber, episode.Label))
            .DistinctBy(episode => episode.EpisodeNumber)
            .ToArray();

        return ordered.Length == 0
            ? []
            :
            [
                new WatchSeasonDto(
                    Id: "season-1",
                    Label: "Season 1",
                    Episodes: ordered)
            ];
    }

    private static IReadOnlyList<string> BuildProviderSequence(string? primaryProvider, ScraperOptions options)
    {
        var providers = new List<string>();

        if (!string.IsNullOrWhiteSpace(primaryProvider))
        {
            providers.Add(primaryProvider.Trim());
        }

        foreach (var fallbackProvider in options.FallbackProviders)
        {
            if (string.IsNullOrWhiteSpace(fallbackProvider))
            {
                continue;
            }

            var provider = fallbackProvider.Trim();
            if (!providers.Contains(provider, StringComparer.OrdinalIgnoreCase))
            {
                providers.Add(provider);
            }
        }

        return providers;
    }

    private static RedisKey BuildCacheKey(Guid animeId) => $"sakugavault:episodes:{animeId:D}";

    private async Task<EpisodeListResult?> TryGetCachedResultAsync(RedisKey cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(cacheKey);
            return value.HasValue
                ? JsonSerializer.Deserialize<EpisodeListResult>(value.ToString(), JsonOptions)
                : null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis episode-list cache read failed for {CacheKey}.", cacheKey);
            return null;
        }
    }

    private async Task TrySetCachedResultAsync(
        RedisKey cacheKey,
        EpisodeListResult result,
        Guid animeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(result, JsonOptions);
            await redis.GetDatabase().StringSetAsync(cacheKey, json, CacheTtl);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis episode-list cache write failed for anime {AnimeId}.", animeId);
        }
    }

    private sealed record ProviderEpisodeAttempt(
        string Provider,
        IReadOnlyCollection<ProviderEpisodeInfo>? Episodes,
        string? Error);
}
