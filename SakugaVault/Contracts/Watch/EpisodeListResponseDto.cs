namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Response for provider-backed episode-list hydration after the watch page renders.
/// </summary>
public sealed record EpisodeListResponseDto(
    bool IsResolved,
    string ProviderKey,
    IReadOnlyCollection<WatchSeasonDto> Seasons,
    string? StatusMessage);
