namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Result of the playback-resolution pipeline.
/// The URL is nullable because the resolver may be intentionally scaffolded before host-specific scrapers are finished.
/// </summary>
public sealed record ResolvedPlaybackDto(
    Guid AnimeId,
    int EpisodeNumber,
    bool IsResolved,
    string PreferredProtocol,
    string? StreamUrl,
    string? SourceHost,
    bool UsedFallback,
    string StatusMessage);
