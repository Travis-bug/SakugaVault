namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Lightweight episode item for the watch-page selector.
/// The frontend uses this to render clickable episode pills instead of a freeform number field.
/// </summary>
public sealed record WatchEpisodeDto(
    int EpisodeNumber,
    string Label);
