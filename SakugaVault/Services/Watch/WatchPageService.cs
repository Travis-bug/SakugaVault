using Microsoft.EntityFrameworkCore;
using SakugaVault.Contracts.Catalog;
using SakugaVault.Contracts.Watch;
using SakugaVault.Data;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Business logic for the watch page.
/// The service composes metadata, playback instructions, comments, and recommendations so the controller
/// only handles routing and status-code decisions.
/// </summary>
public sealed class WatchPageService(SakugaVaultDbContext dbContext) : IWatchPageService
{
    public async Task<WatchPageDto?> GetWatchPageAsync(string animeId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(animeId, out var parsedAnimeId))
        {
            return null;
        }

        var title = await dbContext.Anime
            .AsNoTracking()
            .Include(entry => entry.Comments)
            .ThenInclude(comment => comment.User)
            .Include(entry => entry.AnimeGenres)
            .ThenInclude(link => link.Genre)
            .FirstOrDefaultAsync(entry => entry.Id == parsedAnimeId, cancellationToken);

        if (title is null)
        {
            return null;
        }

        // Playback is resolved on demand so the backend never embeds or stores stream files directly.
        var playback = new PlaybackDescriptorDto(
            PreferredProtocol: "HLS",
            ResolveOnPlay: true,
            ResolverMode: "third-party-scraper");

        var comments = title.Comments
            .OrderByDescending(comment => comment.CreatedAtUtc)
            .Take(25)
            .Select(comment => new CommentDto(
                comment.User.DisplayName,
                comment.Body,
                comment.CreatedAtUtc))
            .ToArray();

        var genreIds = title.AnimeGenres
            .Select(link => link.GenreId)
            .ToArray();

        // Recommendation shaping belongs here because the service owns similar-title rules, not the controller.
        var similarAnime = await dbContext.Anime
            .AsNoTracking()
            .Where(entry => entry.Id != title.Id && entry.AnimeGenres.Any(link => genreIds.Contains(link.GenreId)))
            .OrderBy(entry => entry.TrendingRank)
            .Take(4)
            .Select(entry => new AnimeCardDto(
                entry.Id.ToString(),
                entry.Title,
                entry.PosterImageUrl,
                entry.EpisodeCount,
                entry.SubAvailable,
                entry.DubAvailable,
                $"/watch/{entry.Id}"))
            .ToArrayAsync(cancellationToken);

        var watchPage = new WatchPageDto(
            title.Id.ToString(),
            title.Title,
            title.Synopsis,
            title.PosterImageUrl,
            title.BackdropImageUrl,
            title.RuntimeMinutes,
            title.EpisodeCount,
            title.SubAvailable,
            title.DubAvailable,
            title.MetadataLastSyncedAtUtc,
            playback,
            comments,
            similarAnime);

        return watchPage;
    }
}
