namespace SakugaVault.Services.Watch;

public interface IWatchProgressBuffer
{
    Task<bool> WriteAsync(WatchProgressEntry entry, CancellationToken cancellationToken);
    Task<WatchProgressEntry?> ReadAsync(WatchProgressKey key, CancellationToken cancellationToken);
    Task<IReadOnlyList<WatchProgressKey>> GetDirtyKeysAsync(Guid? userId, CancellationToken cancellationToken);
    Task ClearAsync(WatchProgressKey key, CancellationToken cancellationToken);
}

public sealed record WatchProgressKey(Guid UserId, Guid AnimeId, int EpisodeNumber)
{
    public string ToRedisKeySuffix() => $"{UserId:D}:{AnimeId:D}:{EpisodeNumber}";

    public static bool TryParse(string value, out WatchProgressKey key)
    {
        key = default!;
        var parts = value.Split(':', 3, StringSplitOptions.TrimEntries);
        if (parts.Length != 3 ||
            !Guid.TryParse(parts[0], out var userId) ||
            !Guid.TryParse(parts[1], out var animeId) ||
            !int.TryParse(parts[2], out var episodeNumber))
        {
            return false;
        }

        key = new WatchProgressKey(userId, animeId, episodeNumber);
        return true;
    }
}

public sealed record WatchProgressEntry(
    Guid UserId,
    Guid AnimeId,
    int EpisodeNumber,
    int PositionSeconds,
    int DurationSeconds,
    bool Completed,
    DateTimeOffset LastWatchedAtUtc)
{
    public WatchProgressKey Key => new(UserId, AnimeId, EpisodeNumber);
}
