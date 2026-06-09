using Microsoft.EntityFrameworkCore;
using SakugaVault.Data;
using SakugaVault.Models;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Persists the latest Redis-buffered watch progress into MySQL.
/// This is called on explicit client flushes and by a periodic background worker.
/// </summary>
public sealed class WatchProgressFlushService(
    SakugaVaultDbContext dbContext,
    IWatchProgressBuffer watchProgressBuffer,
    ILogger<WatchProgressFlushService> logger) : IWatchProgressFlushService
{
    public async Task<int> FlushAsync(Guid? userId, CancellationToken cancellationToken)
    {
        var keys = await watchProgressBuffer.GetDirtyKeysAsync(userId, cancellationToken);
        var flushed = 0;

        foreach (var key in keys)
        {
            var entry = await watchProgressBuffer.ReadAsync(key, cancellationToken);
            if (entry is null)
            {
                await watchProgressBuffer.ClearAsync(key, cancellationToken);
                continue;
            }

            try
            {
                var historyEntry = await dbContext.WatchHistoryEntries
                    .FirstOrDefaultAsync(row =>
                        row.UserId == entry.UserId &&
                        row.AnimeId == entry.AnimeId &&
                        row.EpisodeNumber == entry.EpisodeNumber,
                        cancellationToken);

                if (historyEntry is null)
                {
                    historyEntry = new WatchHistoryEntry
                    {
                        UserId = entry.UserId,
                        AnimeId = entry.AnimeId,
                        EpisodeNumber = entry.EpisodeNumber
                    };
                    dbContext.WatchHistoryEntries.Add(historyEntry);
                }

                historyEntry.PositionSeconds = entry.PositionSeconds;
                historyEntry.DurationSeconds = entry.DurationSeconds;
                historyEntry.Completed = entry.Completed;
                historyEntry.LastWatchedAtUtc = entry.LastWatchedAtUtc;

                await dbContext.SaveChangesAsync(cancellationToken);
                await watchProgressBuffer.ClearAsync(key, cancellationToken);
                flushed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Failed to flush watch progress for {ProgressKey}.", key.ToRedisKeySuffix());
                dbContext.ChangeTracker.Clear();
            }
        }

        return flushed;
    }
}
