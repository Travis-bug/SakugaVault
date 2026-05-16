namespace SakugaVault.Models;

/// <summary>
/// Core metadata record for one anime title.
/// The app stores descriptive state here and keeps actual media delivery outside the relational database.
/// </summary>
public sealed class Anime : EntityBase
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Synopsis { get; set; } = string.Empty;
    public string PosterImageUrl { get; set; } = string.Empty;
    public string BackdropImageUrl { get; set; } = string.Empty;
    public int EpisodeCount { get; set; }
    public int RuntimeMinutes { get; set; }
    public bool SubAvailable { get; set; }
    public bool DubAvailable { get; set; }
    public int TrendingRank { get; set; }
    public string? ExternalMetadataId { get; set; }
    public string? MetadataProvider { get; set; }
    public DateTimeOffset? MetadataLastSyncedAtUtc { get; set; }

    public ICollection<AnimeGenre> AnimeGenres { get; } = [];
    public ICollection<AnimeComment> Comments { get; } = [];
    public ICollection<WatchHistoryEntry> WatchHistoryEntries { get; } = [];
}
