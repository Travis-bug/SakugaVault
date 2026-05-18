using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Playback progress update from the client.
/// The watch-history service persists this so users can resume where they left off.
/// </summary>
public sealed record UpsertWatchHistoryRequestDto(
    [param: Required(ErrorMessage = "Anime selection is required.")] Guid AnimeId,
    [param: Range(1, int.MaxValue, ErrorMessage = "Episode number must be at least 1.")] int EpisodeNumber,
    [param: Range(0, int.MaxValue, ErrorMessage = "Playback position cannot be negative.")] int PositionSeconds,
    [param: Range(0, int.MaxValue, ErrorMessage = "Duration cannot be negative.")] int DurationSeconds,
    bool Completed);
