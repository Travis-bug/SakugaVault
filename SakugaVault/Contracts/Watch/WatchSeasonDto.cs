namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Represents one season bucket in the watch-page episode browser.
/// The current provider integration usually maps one provider title to one season, but the contract is ready for multiple buckets.
/// </summary>
public sealed record WatchSeasonDto(
    string Id,
    string Label,
    IReadOnlyCollection<WatchEpisodeDto> Episodes);
