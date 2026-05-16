namespace SakugaVault.Models;

/// <summary>
/// Per-user playback progress for a specific anime episode.
/// This is the persistent backbone for continue-watching rows and resume playback behavior.
/// </summary>
public sealed class WatchHistoryEntry : EntityBase
{
    public Guid AnimeId { get; set; }
    public Guid UserId { get; set; }
    public int EpisodeNumber { get; set; }
    public int PositionSeconds { get; set; }
    public int DurationSeconds { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset LastWatchedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Anime Anime { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
