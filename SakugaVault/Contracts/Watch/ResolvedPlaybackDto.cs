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
    string? Resolver,
    string? AudioLanguage,
    string? SubtitleLanguage,
    bool UsedFallback,
    IReadOnlyCollection<PlaybackSubtitleDto> Subtitles,
    string StatusMessage);

public sealed record PlaybackSubtitleDto(
    string Url,
    string Language,
    string Label,
    string Kind);
