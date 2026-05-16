namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Read model for one watch-history item shown in continue-watching or profile history screens.
/// It includes lightweight anime metadata so the frontend does not need a second lookup per row.
/// </summary>
public sealed record WatchHistoryEntryDto(
    Guid AnimeId,
    string AnimeTitle,
    string PosterImageUrl,
    int EpisodeNumber,
    int PositionSeconds,
    int DurationSeconds,
    bool Completed,
    DateTimeOffset LastWatchedAtUtc);
