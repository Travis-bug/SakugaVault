namespace SakugaVault.Services.Watch;

using SakugaVault.Contracts.Watch;

public interface IEpisodeListService
{
    Task<EpisodeListResult> GetEpisodesAsync(Guid animeId, CancellationToken cancellationToken, bool forceRefresh = false);
}

public sealed record EpisodeListResult(
    bool IsResolved,
    string ProviderKey,
    IReadOnlyCollection<WatchSeasonDto> Seasons,
    string? StatusMessage);
