using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using SakugaVault.Contracts.Catalog;
using SakugaVault.Data;
using SakugaVault.Models;
using SakugaVault.Options;
using SakugaVault.Services.Common;
using SakugaVault.Services.Scraping;

namespace SakugaVault.Services.Catalog;

/// <summary>
/// Business logic for building the catalog home screen response.
/// The catalog now prefers live provider data and only uses MySQL as a shadow store for app state and stable title ids.
/// </summary>
public sealed class CatalogService(
    SakugaVaultDbContext dbContext,
    IMemoryCache cache,
    IAnimeProviderClient animeProviderClient,
    IOptions<CatalogOptions> catalogOptionsAccessor,
    IOptions<ScraperOptions> scraperOptionsAccessor,
    ILogger<CatalogService> logger) : ICatalogService
{
    public const string HomeCatalogCacheKey = "catalog:home";

    private readonly CatalogOptions catalogOptions = catalogOptionsAccessor.Value;
    private readonly ScraperOptions scraperOptions = scraperOptionsAccessor.Value;

    public async Task<HomeCatalogDto> GetHomeCatalogAsync(CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync(HomeCatalogCacheKey, async cacheEntry =>
        {
            cacheEntry.SlidingExpiration = TimeSpan.FromMinutes(catalogOptions.HomeCatalogCacheMinutes);

            if (catalogOptions.UseLiveProviderCatalog)
            {
                var liveCatalog = await TryLoadLiveHomeCatalogAsync(cancellationToken);
                if (liveCatalog is not null)
                {
                    return liveCatalog;
                }

                logger.LogWarning(
                    "Live provider catalog is enabled, but no provider returned a usable home feed. Returning an empty catalog instead of falling back to shadow rows.");
                return EmptyCatalog();
            }

            return await LoadHomeCatalogFromDbAsync(cancellationToken);
        }) ?? EmptyCatalog();
    }

    public async Task<CatalogSearchResponseDto> SearchAsync(string? query, int limit, CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 48);
        var trimmedQuery = query?.Trim() ?? string.Empty;

        if (catalogOptions.UseLiveProviderCatalog)
        {
            var liveResults = string.IsNullOrWhiteSpace(trimmedQuery)
                ? await TryLoadLiveTrendingSearchAsync(normalizedLimit, cancellationToken)
                : await TryLoadLiveSearchAsync(trimmedQuery, normalizedLimit, cancellationToken);

            if (liveResults is not null)
            {
                return liveResults;
            }

            logger.LogWarning(
                "Live provider catalog is enabled, but no provider returned usable search results for query {Query}. Returning an empty provider-backed payload.",
                trimmedQuery);

            return new CatalogSearchResponseDto(trimmedQuery, 0, []);
        }

        return await SearchFromDbAsync(trimmedQuery, normalizedLimit, cancellationToken);
    }

    public async Task<OperationResult<CommentPostedDto>> PostCommentAsync(Guid userId, PostCommentRequestDto request, CancellationToken cancellationToken)
    {
        var animeExists = await dbContext.Anime
            .AsNoTracking()
            .AnyAsync(anime => anime.Id == request.AnimeId, cancellationToken);

        if (!animeExists)
        {
            return OperationResult<CommentPostedDto>.Failure("anime_not_found", "The requested anime could not be found.");
        }

        var author = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

        if (author is null)
        {
            return OperationResult<CommentPostedDto>.Failure("user_not_found", "The authenticated user could not be found.");
        }

        var comment = new AnimeComment
        {
            AnimeId = request.AnimeId,
            UserId = userId,
            Body = request.Body.Trim()
        };

        dbContext.AnimeComments.Add(comment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<CommentPostedDto>.Success(
            new CommentPostedDto(
                comment.Id,
                comment.AnimeId,
                author.DisplayName,
                comment.Body,
                comment.CreatedAtUtc));
    }

    private async Task<HomeCatalogDto?> TryLoadLiveHomeCatalogAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in BuildProviderSequence())
        {
            var feedTitles = await animeProviderClient.GetFeedAsync(
                provider,
                catalogOptions.HomeFeed,
                catalogOptions.HomePageCount,
                cancellationToken);

            if (feedTitles.Count == 0)
            {
                continue;
            }

            var hydratedTitles = await HydrateLiveTitlesAsync(provider, feedTitles, cancellationToken);
            if (hydratedTitles.Count == 0)
            {
                continue;
            }

            return BuildLiveHomeCatalog(hydratedTitles);
        }

        logger.LogWarning("Live provider catalog loading failed for every configured provider. Falling back to the database-backed catalog.");
        return null;
    }

    private async Task<CatalogSearchResponseDto?> TryLoadLiveTrendingSearchAsync(int limit, CancellationToken cancellationToken)
    {
        foreach (var provider in BuildProviderSequence())
        {
            var feedTitles = await animeProviderClient.GetFeedAsync(
                provider,
                catalogOptions.HomeFeed,
                1,
                cancellationToken);

            if (feedTitles.Count == 0)
            {
                continue;
            }

            var hydratedTitles = await HydrateLiveTitlesAsync(provider, feedTitles.Take(limit).ToArray(), cancellationToken);
            if (hydratedTitles.Count == 0)
            {
                continue;
            }

            return new CatalogSearchResponseDto(
                string.Empty,
                hydratedTitles.Count,
                hydratedTitles
                    .Take(limit)
                    .Select(ToSearchResult)
                    .ToArray());
        }

        return null;
    }

    private async Task<CatalogSearchResponseDto?> TryLoadLiveSearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        foreach (var provider in BuildProviderSequence())
        {
            var results = await animeProviderClient.SearchAsync(provider, query, 1, cancellationToken);
            if (results.Count == 0)
            {
                continue;
            }

            var hydratedTitles = await HydrateLiveTitlesAsync(provider, results.Take(limit).ToArray(), cancellationToken);
            if (hydratedTitles.Count == 0)
            {
                continue;
            }

            return new CatalogSearchResponseDto(
                query,
                hydratedTitles.Count,
                hydratedTitles
                    .Take(limit)
                    .Select(ToSearchResult)
                    .ToArray());
        }

        logger.LogWarning(
            "Live provider search returned no usable results for query {Query}. Falling back to the database-backed search path.",
            query);
        return null;
    }

    private async Task<IReadOnlyCollection<LiveCatalogTitle>> HydrateLiveTitlesAsync(
        string provider,
        IEnumerable<ProviderCatalogTitle> sourceTitles,
        CancellationToken cancellationToken)
    {
        var selectedTitles = sourceTitles
            .Take(Math.Max(1, catalogOptions.LiveCatalogTitleLimit))
            .ToArray();

        if (selectedTitles.Length == 0)
        {
            return [];
        }

        var hydratedTitles = new List<LiveCatalogTitle>(selectedTitles.Length);
        foreach (var title in selectedTitles)
        {
            // BYPASS THE BROKEN INFO ROUTE:
            // We skip GetAnimeInfoAsync entirely to prevent the 500 error lag.
            ProviderAnimeInfo? metadata = null;

            // Use the genres we already grabbed from the search feed.
            var genres = title.Genres.Count > 0 ? title.Genres : Array.Empty<string>();

            var shadowAnime = await UpsertShadowAnimeAsync(
                title,
                metadata,
                hydratedTitles.Count + 1,
                cancellationToken);

            hydratedTitles.Add(new LiveCatalogTitle(
                shadowAnime.Id,
                title.Provider,
                title.ExternalId,
                shadowAnime.Title,
                metadata?.Synopsis ?? shadowAnime.Synopsis,
                shadowAnime.PosterImageUrl,
                shadowAnime.BackdropImageUrl,
                shadowAnime.EpisodeCount,
                shadowAnime.SubAvailable,
                shadowAnime.DubAvailable,
                hydratedTitles.Count + 1,
                genres));
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return hydratedTitles;
    }

    private async Task<Anime> UpsertShadowAnimeAsync(
        ProviderCatalogTitle sourceTitle,
        ProviderAnimeInfo? metadata,
        int trendingRank,
        CancellationToken cancellationToken)
    {
        var existingAnime = await dbContext.Anime
            .Include(anime => anime.AnimeGenres)
            .FirstOrDefaultAsync(
                anime => anime.MetadataProvider == sourceTitle.Provider && anime.ExternalMetadataId == sourceTitle.ExternalId,
                cancellationToken);

        Anime anime;
        if (existingAnime is null)
        {
            anime = new Anime
            {
                Slug = await BuildUniqueSlugAsync(metadata?.Title ?? sourceTitle.Title, sourceTitle.ExternalId, cancellationToken)
            };

            dbContext.Anime.Add(anime);
        }
        else
        {
            anime = existingAnime;
        }

        anime.Title = metadata?.Title ?? sourceTitle.Title;
        anime.Synopsis = string.IsNullOrWhiteSpace(metadata?.Synopsis)
            ? $"Live title loaded from provider {sourceTitle.Provider}."
            : metadata!.Synopsis;
        anime.PosterImageUrl = !string.IsNullOrWhiteSpace(metadata?.PosterImageUrl)
            ? metadata.PosterImageUrl
            : sourceTitle.ImageUrl;
        anime.BackdropImageUrl = !string.IsNullOrWhiteSpace(metadata?.BackdropImageUrl)
            ? metadata.BackdropImageUrl
            : anime.PosterImageUrl;
        anime.EpisodeCount = metadata?.EpisodeCount > 0
            ? metadata.EpisodeCount
            : Math.Max(sourceTitle.EpisodeCount, 1);
        anime.RuntimeMinutes = anime.RuntimeMinutes > 0 ? anime.RuntimeMinutes : 24;
        anime.SubAvailable = metadata?.SubAvailable ?? sourceTitle.SubAvailable;
        anime.DubAvailable = metadata?.DubAvailable ?? sourceTitle.DubAvailable;
        anime.TrendingRank = trendingRank;
        anime.MetadataProvider = sourceTitle.Provider;
        anime.ExternalMetadataId = sourceTitle.ExternalId;

        await SyncGenresAsync(anime, metadata?.Genres ?? Array.Empty<string>(), cancellationToken);
        return anime;
    }

    private async Task SyncGenresAsync(Anime anime, IReadOnlyCollection<string> genres, CancellationToken cancellationToken)
    {
        if (anime.Id != Guid.Empty && anime.AnimeGenres.Count > 0)
        {
            dbContext.AnimeGenres.RemoveRange(anime.AnimeGenres);
            anime.AnimeGenres.Clear();
        }

        if (genres.Count == 0)
        {
            return;
        }

        var genreSlugs = genres
            .Select(Slugify)
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existingGenres = await dbContext.Genres
            .Where(genre => genreSlugs.Contains(genre.Slug))
            .ToDictionaryAsync(genre => genre.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var genreName in genres.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var slug = Slugify(genreName);
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            if (!existingGenres.TryGetValue(slug, out var genre))
            {
                genre = new Genre
                {
                    Name = genreName.Trim(),
                    Slug = slug
                };

                dbContext.Genres.Add(genre);
                existingGenres[slug] = genre;
            }

            anime.AnimeGenres.Add(new AnimeGenre
            {
                Anime = anime,
                Genre = genre
            });
        }
    }

    private async Task<string> BuildUniqueSlugAsync(string title, string fallbackExternalId, CancellationToken cancellationToken)
    {
        var baseSlug = Slugify(title);
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            baseSlug = Slugify(fallbackExternalId);
        }

        var existingSlugs = new HashSet<string>(
            await dbContext.Anime
                .AsNoTracking()
                .Select(anime => anime.Slug)
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        var slug = baseSlug;
        var counter = 2;
        while (!existingSlugs.Add(slug))
        {
            slug = $"{baseSlug}-{counter++}";
        }

        return slug;
    }

    private async Task<HomeCatalogDto> LoadHomeCatalogFromDbAsync(CancellationToken cancellationToken)
    {
        var topAnime = await dbContext.Anime
            .AsNoTracking()
            .Include(anime => anime.AnimeGenres)
            .ThenInclude(link => link.Genre)
            .OrderBy(anime => anime.TrendingRank)
            .ThenBy(anime => anime.Title)
            .Take(100)
            .ToArrayAsync(cancellationToken);

        if (topAnime.Length == 0)
        {
            return EmptyCatalog();
        }

        var heroItems = topAnime
            .Take(5)
            .Select(ToHeroItem)
            .ToArray();

        var genreRows = topAnime
            .SelectMany(title => title.AnimeGenres.Select(link => new { Genre = link.Genre.Name, Title = title }))
            .GroupBy(entry => entry.Genre, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .Select(group => new GenreRailDto(
                group.Key,
                group
                    .OrderBy(entry => entry.Title.TrendingRank)
                    .Select(entry => ToAnimeCard(entry.Title))
                    .ToArray()))
            .ToArray();

        return new HomeCatalogDto(heroItems, genreRows);
    }

    private async Task<CatalogSearchResponseDto> SearchFromDbAsync(string trimmedQuery, int normalizedLimit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            var trendingTitles = await dbContext.Anime
                .AsNoTracking()
                .Include(anime => anime.AnimeGenres)
                .ThenInclude(link => link.Genre)
                .OrderBy(anime => anime.TrendingRank)
                .ThenBy(anime => anime.Title)
                .Take(normalizedLimit)
                .ToArrayAsync(cancellationToken);

            return new CatalogSearchResponseDto(
                string.Empty,
                trendingTitles.Length,
                trendingTitles.Select(ToSearchResult).ToArray());
        }

        var queryPattern = $"%{trimmedQuery}%";
        var searchQuery = dbContext.Anime
            .AsNoTracking()
            .Include(anime => anime.AnimeGenres)
            .ThenInclude(link => link.Genre)
            .Where(anime =>
                EF.Functions.Like(anime.Title, queryPattern) ||
                EF.Functions.Like(anime.Synopsis, queryPattern) ||
                EF.Functions.Like(anime.Slug, queryPattern));

        var totalResults = await searchQuery.CountAsync(cancellationToken);
        var results = await searchQuery
            .OrderBy(anime => anime.Title == trimmedQuery ? 0 : 1)
            .ThenBy(anime => anime.Title.StartsWith(trimmedQuery) ? 0 : 1)
            .ThenBy(anime => anime.TrendingRank)
            .ThenBy(anime => anime.Title)
            .Take(normalizedLimit)
            .ToArrayAsync(cancellationToken);

        return new CatalogSearchResponseDto(
            trimmedQuery,
            totalResults,
            results.Select(ToSearchResult).ToArray());
    }

    private HomeCatalogDto BuildLiveHomeCatalog(IReadOnlyCollection<LiveCatalogTitle> titles)
    {
        if (titles.Count == 0)
        {
            return EmptyCatalog();
        }

        var orderedTitles = titles
            .OrderBy(title => title.TrendingRank)
            .ThenBy(title => title.Title)
            .ToArray();

        var heroItems = orderedTitles
            .Take(5)
            .Select(ToHeroItem)
            .ToArray();

        var genreRows = orderedTitles
            .SelectMany(title => title.Genres.Select(genre => new { Genre = genre, Title = title }))
            .GroupBy(entry => entry.Genre, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .Select(group => new GenreRailDto(
                group.Key,
                group
                    .OrderBy(entry => entry.Title.TrendingRank)
                    .Select(entry => ToAnimeCard(entry.Title))
                    .ToArray()))
            .Where(rail => rail.Titles.Count > 0)
            .ToArray();

        return new HomeCatalogDto(heroItems, genreRows);
    }

    private static HomeCatalogDto EmptyCatalog()
    {
        return new HomeCatalogDto(
            [],
            []);
    }

    private static CatalogHeroDto ToHeroItem(Anime title)
    {
        return new CatalogHeroDto(
            title.Id.ToString(),
            title.Title,
            title.Synopsis,
            title.PosterImageUrl,
            title.BackdropImageUrl,
            $"/watch/{title.Id}");
    }

    private static CatalogHeroDto ToHeroItem(LiveCatalogTitle title)
    {
        return new CatalogHeroDto(
            title.ShadowAnimeId.ToString(),
            title.Title,
            title.Synopsis,
            title.PosterImageUrl,
            title.BackdropImageUrl,
            $"/watch/{title.ShadowAnimeId}");
    }

    private static AnimeCardDto ToAnimeCard(Anime title)
    {
        return new AnimeCardDto(
            title.Id.ToString(),
            title.Title,
            title.PosterImageUrl,
            title.EpisodeCount,
            title.SubAvailable,
            title.DubAvailable,
            $"/watch/{title.Id}");
    }

    private static AnimeCardDto ToAnimeCard(LiveCatalogTitle title)
    {
        return new AnimeCardDto(
            title.ShadowAnimeId.ToString(),
            title.Title,
            title.PosterImageUrl,
            title.EpisodeCount,
            title.SubAvailable,
            title.DubAvailable,
            $"/watch/{title.ShadowAnimeId}");
    }

    private static SearchAnimeResultDto ToSearchResult(Anime title)
    {
        return new SearchAnimeResultDto(
            title.Id.ToString(),
            title.Title,
            title.Synopsis,
            title.PosterImageUrl,
            title.EpisodeCount,
            title.SubAvailable,
            title.DubAvailable,
            $"/watch/{title.Id}",
            title.AnimeGenres
                .Select(link => link.Genre.Name)
                .OrderBy(name => name)
                .ToArray());
    }

    private static SearchAnimeResultDto ToSearchResult(LiveCatalogTitle title)
    {
        return new SearchAnimeResultDto(
            title.ShadowAnimeId.ToString(),
            title.Title,
            title.Synopsis,
            title.PosterImageUrl,
            title.EpisodeCount,
            title.SubAvailable,
            title.DubAvailable,
            $"/watch/{title.ShadowAnimeId}",
            title.Genres
                .OrderBy(name => name)
                .ToArray());
    }

    private IReadOnlyCollection<string> BuildProviderSequence()
    {
        var providers = new List<string>();

        foreach (var provider in catalogOptions.PreferredProviders)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                continue;
            }

            if (providers.Contains(provider, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            providers.Add(provider.Trim());
        }

        foreach (var provider in scraperOptions.FallbackProviders)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                continue;
            }

            if (providers.Contains(provider, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            providers.Add(provider.Trim());
        }

        if (providers.Count == 0)
        {
            providers.Add("gogoanime");
        }

        return providers;
    }

    private static string Slugify(string value)
    {
        var slug = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }

    private sealed record LiveCatalogTitle(
        Guid ShadowAnimeId,
        string Provider,
        string ExternalId,
        string Title,
        string Synopsis,
        string PosterImageUrl,
        string BackdropImageUrl,
        int EpisodeCount,
        bool SubAvailable,
        bool DubAvailable,
        int TrendingRank,
        IReadOnlyCollection<string> Genres);
}
