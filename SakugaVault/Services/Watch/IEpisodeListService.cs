namespace SakugaVault.Services.Watch;

using SakugaVault.Contracts.Watch;

public interface IEpisodeListService
{
    Task<EpisodeListResult> GetEpisodesAsync(Guid animeId, CancellationToken cancellationToken);
}

public sealed record EpisodeListResult(
    bool IsResolved,
    string ProviderKey,
    IReadOnlyCollection<WatchSeasonDto> Seasons,
    string? StatusMessage);
