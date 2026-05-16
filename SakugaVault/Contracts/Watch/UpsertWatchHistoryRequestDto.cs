using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Playback progress update from the client.
/// The watch-history service persists this so users can resume where they left off.
/// </summary>
public sealed record UpsertWatchHistoryRequestDto(
    [property: Required] Guid AnimeId,
    [property: Range(1, int.MaxValue)] int EpisodeNumber,
    [property: Range(0, int.MaxValue)] int PositionSeconds,
    [property: Range(0, int.MaxValue)] int DurationSeconds,
    bool Completed);
