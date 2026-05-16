using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using SakugaVault.Contracts.Catalog;
using SakugaVault.Data;
using SakugaVault.Models;
using SakugaVault.Options;
using SakugaVault.Services.Common;

namespace SakugaVault.Services.Catalog;

/// <summary>
/// Business logic for building the catalog home screen response.
/// The service owns content ranking and genre-row aggregation because those are application rules,
/// which is exactly what the thin-controller/fat-service refactor was meant to enforce.
/// </summary>
public sealed class CatalogService(
    SakugaVaultDbContext dbContext,
    IMemoryCache cache,
    IOptions<CatalogOptions> catalogOptionsAccessor) : ICatalogService
{
    public const string HomeCatalogCacheKey = "catalog:home";

    private readonly CatalogOptions catalogOptions = catalogOptionsAccessor.Value;

    public async Task<HomeCatalogDto> GetHomeCatalogAsync(CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync(HomeCatalogCacheKey, async cacheEntry =>
        {
            cacheEntry.SlidingExpiration = TimeSpan.FromMinutes(catalogOptions.HomeCatalogCacheMinutes);
            return await LoadHomeCatalogAsync(cancellationToken);
        }) ?? EmptyCatalog();
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

    private async Task<HomeCatalogDto> LoadHomeCatalogAsync(CancellationToken cancellationToken)
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

        var featuredTitle = topAnime[0];

        // The hero banner is computed here so the controller never needs to know which title is "featured".
        var heroBanner = new CatalogHeroDto(
            featuredTitle.Id.ToString(),
            featuredTitle.Title,
            featuredTitle.Synopsis,
            featuredTitle.PosterImageUrl,
            featuredTitle.BackdropImageUrl,
            $"/watch/{featuredTitle.Id}");

        // Genre rows are a catalog-specific view model concern. Grouping them inside the service keeps
        // screen composition logic out of endpoints and prepares the code for a database-backed query layer.
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

        return new HomeCatalogDto(heroBanner, genreRows);
    }

    private static HomeCatalogDto EmptyCatalog()
    {
        return new HomeCatalogDto(
            new CatalogHeroDto(
                Id: string.Empty,
                Title: string.Empty,
                Synopsis: string.Empty,
                PosterImageUrl: string.Empty,
                BackdropImageUrl: string.Empty,
                WatchRoute: string.Empty),
            []);
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
}
