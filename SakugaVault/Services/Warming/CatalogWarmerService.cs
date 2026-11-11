using Microsoft.EntityFrameworkCore;
using SakugaVault.Contracts.Catalog;
using SakugaVault.Data;
using SakugaVault.Services.Catalog;
using SakugaVault.Services.Redis;
using SakugaVault.Services.Scraping;
using SakugaVault.Services.Watch;

namespace SakugaVault.Services.Warming;

/// <summary>
/// Background cache warmer — an always-on "virtual viewer" for the most popular
/// titles. Every interval it re-warms their episode lists (so the list never
/// shows "temporarily unavailable" / a stale single episode) and, optionally,
/// pre-resolves episode 1 (which warms the kwik CDN key + first segments and
/// the resolution cache) so the first real viewer gets instant playback.
///
/// Runs entirely in-process: no fake account, no HTTP, no auth, and it does NOT
/// go through the per-user/global scrape rate limiters, so it never starves real
/// users. It stays gentle (small title count, delay between titles) to avoid
/// hammering AnimePahe/FlareSolverr from the datacenter IP. All tunables come
/// from the "Warmer:*" config section; disable entirely with Warmer:Enabled=false.
/// </summary>
public sealed class CatalogWarmerService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<CatalogWarmerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Warmer:Enabled", true))
        {
            logger.LogInformation("Catalog warmer disabled (Warmer:Enabled=false).");
            return;
        }

        // Belt-and-suspenders: an unhandled exception from a BackgroundService
        // stops the whole host by default. Never let the warmer take down the API.
        try
        {
            var startupDelay = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue("Warmer:StartupDelaySeconds", 120)));
            var interval = TimeSpan.FromHours(Math.Max(0.25, configuration.GetValue("Warmer:IntervalHours", 3.0)));
            logger.LogInformation(
                "Catalog warmer starting: first pass in {StartupDelay}, then every {Interval}.",
                startupDelay,
                interval);

            if (!await DelayAsync(startupDelay, stoppingToken))
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await WarmOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Catalog warmer pass failed; retrying next interval.");
                }

                if (!await DelayAsync(interval, stoppingToken))
                {
                    break;
                }
            }
        }
        catch (Exception fatal)
        {
            // Swallow so the host stays up; warming simply stops until next restart.
            logger.LogError(fatal, "Catalog warmer terminated unexpectedly; warming disabled until API restart.");
        }
    }

    private async Task WarmOnceAsync(CancellationToken cancellationToken)
    {
        var titleCount = Math.Max(1, configuration.GetValue("Warmer:TitleCount", 30));
        var perTitleDelay = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Warmer:PerTitleDelaySeconds", 8)));
        var warmPlayback = configuration.GetValue("Warmer:WarmPlayback", false);
        var playbackCount = Math.Max(0, configuration.GetValue("Warmer:PlaybackTitleCount", 8));

        List<Guid> ids;
        using (var scope = scopeFactory.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var home = await catalog.GetHomeCatalogAsync(cancellationToken);
            ids = ExtractAnimeIds(home).Take(titleCount).ToList();
        }

        if (ids.Count == 0)
        {
            logger.LogInformation("Catalog warmer: no titles available to warm this pass.");
            return;
        }

        logger.LogInformation(
            "Catalog warmer: warming {Count} titles (playback pre-warm: {WarmPlayback}, first {PlaybackCount}).",
            ids.Count,
            warmPlayback,
            warmPlayback ? playbackCount : 0);

        var warmedLists = 0;
        var warmedPlayback = 0;

        for (var index = 0; index < ids.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var animeId = ids[index];

            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;

            try
            {
                var episodeList = services.GetRequiredService<IEpisodeListService>();
                // Force-refresh so each pass re-fetches (picking up new episodes)
                // and re-warms the cache, rather than returning the cached copy.
                var episodes = await episodeList.GetEpisodesAsync(animeId, cancellationToken, forceRefresh: true);
                if (episodes.IsResolved)
                {
                    warmedLists++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Episode-list warm failed for {AnimeId}.", animeId);
            }

            if (warmPlayback && index < playbackCount)
            {
                try
                {
                    if (await WarmPlaybackAsync(services, animeId, cancellationToken))
                    {
                        warmedPlayback++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Playback warm failed for {AnimeId}.", animeId);
                }
            }

            if (!await DelayAsync(perTitleDelay, cancellationToken))
            {
                break;
            }
        }

        logger.LogInformation(
            "Catalog warmer pass complete: {Lists}/{Total} episode lists warmed, {Playback} playback pre-warms.",
            warmedLists,
            ids.Count,
            warmedPlayback);
    }

    /// <summary>
    /// Resolves episode 1 through the scraper (which triggers the resolver's CDN
    /// pre-warm of the m3u8/key/first segments) and caches the result under the
    /// same key <c>PlaybackResolutionService</c> uses, so a real viewer's resolve
    /// is an immediate cache hit. Does not register a user stream, so no HTTP
    /// context is required.
    /// </summary>
    private static async Task<bool> WarmPlaybackAsync(IServiceProvider services, Guid animeId, CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<SakugaVaultDbContext>();
        var anime = await dbContext.Anime
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == animeId, cancellationToken);
        if (anime is null)
        {
            return false;
        }

        var scraper = services.GetRequiredService<IStreamScraperService>();
        var result = await scraper.ResolveStreamAsync(
            anime,
            episodeNumber: 1,
            preferredLanguage: "sub",
            audioLanguage: "ja",
            subtitleLanguage: "en",
            allowRegionalFallback: false,
            providerOverride: anime.MetadataProvider,
            cancellationToken);

        if (!result.IsResolved)
        {
            return false;
        }

        var cache = services.GetRequiredService<IStreamCacheService>();
        var key = new StreamCacheKey(animeId, 1, "sub", "ja", "en", false, null);
        await cache.SetAsync(key, result, cancellationToken);
        return true;
    }

    private static IEnumerable<Guid> ExtractAnimeIds(HomeCatalogDto home)
    {
        var seen = new HashSet<Guid>();
        foreach (var hero in home.HeroItems)
        {
            if (Guid.TryParse(hero.Id, out var heroId) && seen.Add(heroId))
            {
                yield return heroId;
            }
        }

        foreach (var row in home.GenreRows)
        {
            foreach (var card in row.Titles)
            {
                if (Guid.TryParse(card.Id, out var cardId) && seen.Add(cardId))
                {
                    yield return cardId;
                }
            }
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
