using Microsoft.EntityFrameworkCore;
using SakugaVault.Contracts.Catalog;
using SakugaVault.Contracts.Watch;
using SakugaVault.Data;
using Microsoft.Extensions.Options;
using SakugaVault.Options;
using SakugaVault.Services.Scraping;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Business logic for the watch page.
/// The service composes metadata, playback instructions, comments, and recommendations so the controller
/// only handles routing and status-code decisions.
/// </summary>
public sealed class WatchPageService(
    SakugaVaultDbContext dbContext,
    IAnimeProviderClient animeProviderClient,
    IOptions<ScraperOptions> scraperOptionsAccessor,
    ILogger<WatchPageService> logger) : IWatchPageService
{
    private readonly ScraperOptions scraperOptions = scraperOptionsAccessor.Value;

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

        var providerInfo = await TryGetProviderInfoAsync(title, cancellationToken);

        // Playback is resolved on demand so the backend never embeds or stores stream files directly.
        var playback = new PlaybackDescriptorDto(
            PreferredProtocol: "HLS",
            ResolveOnPlay: true,
            ResolverMode: "Consumet");

        var comments = title.Comments
            .OrderByDescending(comment => comment.CreatedAtUtc)
            .Take(25)
            .Select(comment => new CommentDto(
                comment.User.DisplayName,
                comment.Body,
                comment.CreatedAtUtc))
            .ToArray();

        var seasons = BuildSeasons(providerInfo);

        // Keep the similar-title lookup inside SQL instead of parameterizing a local Guid array.
        // The Oracle MySQL EF provider was throwing a null-reference exception when binding the local array.
        var titleGenreIds = dbContext.AnimeGenres
            .AsNoTracking()
            .Where(link => link.AnimeId == title.Id)
            .Select(link => link.GenreId);

        // Recommendation shaping belongs here because the service owns similar-title rules, not the controller.
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

        var subAvailable = DetermineSubAvailability(providerInfo, title.SubAvailable);
        var dubAvailable = DetermineDubAvailability(providerInfo, title.DubAvailable);

        var watchPage = new WatchPageDto(
            title.Id.ToString(),
            SelectValue(providerInfo?.Title, title.Title),
            SelectValue(providerInfo?.Synopsis, title.Synopsis),
            SelectValue(providerInfo?.PosterImageUrl, title.PosterImageUrl),
            SelectValue(providerInfo?.BackdropImageUrl, providerInfo?.PosterImageUrl, title.BackdropImageUrl),
            title.RuntimeMinutes,
            providerInfo?.EpisodeCount ?? providerInfo?.Episodes?.Count ?? title.EpisodeCount,
            subAvailable,
            dubAvailable,
            BuildAudioLanguages(subAvailable, dubAvailable),
            BuildSubtitleLanguages(subAvailable),
            title.MetadataLastSyncedAtUtc,
            playback,
            seasons,
            comments,
            similarAnime);

        return watchPage;
    }

    private async Task<ProviderAnimeInfo?> TryGetProviderInfoAsync(Models.Anime title, CancellationToken cancellationToken)
    {
        foreach (var provider in BuildProviderSequence(title.MetadataProvider))
        {
            ProviderAnimeInfo? providerInfo;
            if (string.Equals(provider, title.MetadataProvider, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(title.ExternalMetadataId))
            {
                providerInfo = await animeProviderClient.GetAnimeInfoAsync(provider, title.ExternalMetadataId, cancellationToken);
            }
            else
            {
                providerInfo = await animeProviderClient.FindAnimeInfoByTitleAsync(provider, title.Title, cancellationToken);
            }

            if (providerInfo is not null)
            {
                logger.LogInformation(
                    "Watch page metadata resolved for anime {AnimeId} using provider {Provider}",
                    title.Id,
                    provider);
                return providerInfo;
            }
        }

        logger.LogWarning(
            "Watch page metadata lookup failed for anime {AnimeId} across every configured provider",
            title.Id);
        return null;
    }

    private IReadOnlyCollection<string> BuildProviderSequence(string? primaryProvider)
    {
        var providers = new List<string>();

        if (!string.IsNullOrWhiteSpace(primaryProvider))
        {
            providers.Add(primaryProvider.Trim());
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

    private static IReadOnlyCollection<WatchSeasonDto> BuildSeasons(ProviderAnimeInfo? providerInfo)
    {
        var episodes = providerInfo?.Episodes?
            .OrderBy(episode => episode.EpisodeNumber)
            .Select(episode => new WatchEpisodeDto(
                episode.EpisodeNumber,
                episode.Label))
            .DistinctBy(episode => episode.EpisodeNumber)
            .ToArray() ?? [];

        if (episodes.Length == 0)
        {
            return [];
        }

        return
        [
            new WatchSeasonDto(
                Id: "season-1",
                Label: "Season 1",
                Episodes: episodes)
        ];
    }

    private static string SelectValue(string? primary, string fallback)
    {
        return string.IsNullOrWhiteSpace(primary) ? fallback : primary.Trim();
    }

    private static string SelectValue(string? primary, string? secondary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            return secondary.Trim();
        }

        return fallback;
    }

    private static bool DetermineSubAvailability(ProviderAnimeInfo? providerInfo, bool fallback)
    {
        return providerInfo?.SubAvailable ?? fallback;
    }

    private static bool DetermineDubAvailability(ProviderAnimeInfo? providerInfo, bool fallback)
    {
        return providerInfo?.DubAvailable ?? fallback;
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
