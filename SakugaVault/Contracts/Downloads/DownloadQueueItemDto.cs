namespace SakugaVault.Contracts.Downloads;

/// <summary>
/// Read model for one queued download request.
/// This keeps the downloads page independent from EF entities while still exposing enough context
/// to render queue state and jump back to the watch page.
/// </summary>
public sealed record DownloadQueueItemDto(
    Guid DownloadId,
    Guid AnimeId,
    string AnimeTitle,
    string PosterImageUrl,
    int EpisodeNumber,
    string PreferredLanguage,
    string Quality,
    string Status,
    DateTimeOffset RequestedAtUtc,
    string WatchRoute);
