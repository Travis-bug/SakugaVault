namespace SakugaVault.Models;

/// <summary>
/// Persistence model for a user's requested episode download.
/// SakugaVault is not transferring media files yet, but the queue itself is real application state
/// that belongs in MySQL so the React downloads screen has durable data.
/// </summary>
public sealed class DownloadRequest : EntityBase
{
    public Guid AnimeId { get; set; }
    public Anime Anime { get; set; } = null!;

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int EpisodeNumber { get; set; }
    public string PreferredLanguage { get; set; } = "sub";
    public string Quality { get; set; } = "1080p";
    public string Status { get; set; } = "Queued";
}
