using Microsoft.EntityFrameworkCore;
using SakugaVault.Contracts.Catalog;
using SakugaVault.Contracts.Watch;
using SakugaVault.Data;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Assembles the watch page entirely from the local database.
/// Provider episode lookup happens after render through the episode-list endpoint.
/// </summary>
public sealed class WatchPageService(
    SakugaVaultDbContext dbContext,
    ILogger<WatchPageService> logger) : IWatchPageService
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

        var comments = title.Comments
            .OrderByDescending(comment => comment.CreatedAtUtc)
            .Take(25)
            .Select(comment => new CommentDto(
                comment.User.DisplayName,
                comment.Body,
                comment.CreatedAtUtc))
            .ToArray();

        // Keep the similar-title lookup inside SQL instead of parameterizing a local Guid array.
        // The Oracle MySQL EF provider was throwing a null-reference exception when binding the local array.
        var titleGenreIds = dbContext.AnimeGenres
            .AsNoTracking()
            .Where(link => link.AnimeId == title.Id)
            .Select(link => link.GenreId);

        var similarAnime = await dbContext.Anime
            .AsNoTracking()
            .Where(entry => entry.Id != title.Id && entry.AnimeGenres.Any(link => titleGenreIds.Contains(link.GenreId)))
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

        var playback = new PlaybackDescriptorDto(
            PreferredProtocol: "HLS",
            ResolveOnPlay: true,
            ResolverMode: "Consumet");

        logger.LogDebug("Watch page for anime {AnimeId} loaded from DB without provider calls.", parsedAnimeId);

        return new WatchPageDto(
            title.Id.ToString(),
            title.Title,
            title.Synopsis,
            title.PosterImageUrl,
            title.BackdropImageUrl,
            title.RuntimeMinutes,
            title.EpisodeCount,
            title.SubAvailable,
            title.DubAvailable,
            BuildAudioLanguages(title.SubAvailable, title.DubAvailable),
            BuildSubtitleLanguages(title.SubAvailable),
            title.MetadataLastSyncedAtUtc,
            playback,
            BuildPlaceholderSeasons(title.EpisodeCount),
            comments,
            similarAnime);
    }

    private static IReadOnlyCollection<WatchSeasonDto> BuildPlaceholderSeasons(int episodeCount)
    {
        if (episodeCount <= 0)
        {
            return [];
        }

        var episodes = Enumerable.Range(1, episodeCount)
            .Select(episodeNumber => new WatchEpisodeDto(episodeNumber, $"Episode {episodeNumber}"))
            .ToArray();

        return
        [
            new WatchSeasonDto(
                Id: "season-1",
                Label: "Season 1",
                Episodes: episodes)
        ];
    }

    private static IReadOnlyCollection<string> BuildAudioLanguages(bool subAvailable, bool dubAvailable)
    {
        var languages = new List<string>();

        if (dubAvailable)
        {
            languages.Add("en");
        }

        if (subAvailable)
        {
            languages.Add("ja");
        }

        return languages.Count > 0 ? languages : ["ja"];
    }

    private static IReadOnlyCollection<string> BuildSubtitleLanguages(bool subAvailable)
    {
        return subAvailable ? ["en"] : [];
    }
}
