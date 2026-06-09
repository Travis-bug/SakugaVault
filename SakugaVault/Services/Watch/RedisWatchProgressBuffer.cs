using System.Text.Json;
using StackExchange.Redis;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Buffers high-frequency playback progress in Redis so timeupdate events do not spam MySQL.
/// Redis failures return false so callers can fall back to the existing durable write path.
/// </summary>
public sealed class RedisWatchProgressBuffer(
    IConnectionMultiplexer redis,
    ILogger<RedisWatchProgressBuffer> logger) : IWatchProgressBuffer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan EntryTtl = TimeSpan.FromHours(24);

    public async Task<bool> WriteAsync(WatchProgressEntry entry, CancellationToken cancellationToken)
    {
        var database = redis.GetDatabase();
        var suffix = entry.Key.ToRedisKeySuffix();

        try
        {
            var batch = database.CreateBatch();
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            var entryTask = batch.StringSetAsync(BuildEntryKey(entry.Key), json, EntryTtl);
            var globalDirtyTask = batch.SetAddAsync(BuildGlobalDirtySetKey(), suffix);
            var userDirtyTask = batch.SetAddAsync(BuildUserDirtySetKey(entry.UserId), suffix);
            var globalExpireTask = batch.KeyExpireAsync(BuildGlobalDirtySetKey(), EntryTtl);
            var userExpireTask = batch.KeyExpireAsync(BuildUserDirtySetKey(entry.UserId), EntryTtl);
            batch.Execute();

            await Task.WhenAll(entryTask, globalDirtyTask, userDirtyTask, globalExpireTask, userExpireTask);
            return entryTask.Result;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis watch-progress buffer write failed for {ProgressKey}; falling back to MySQL.", suffix);
            return false;
        }
    }

    public async Task<WatchProgressEntry?> ReadAsync(WatchProgressKey key, CancellationToken cancellationToken)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(BuildEntryKey(key));
            return value.HasValue
                ? JsonSerializer.Deserialize<WatchProgressEntry>(value.ToString(), JsonOptions)
                : null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis watch-progress buffer read failed for {ProgressKey}.", key.ToRedisKeySuffix());
            return null;
        }
    }

    public async Task<IReadOnlyList<WatchProgressKey>> GetDirtyKeysAsync(Guid? userId, CancellationToken cancellationToken)
    {
        try
        {
            var members = await redis.GetDatabase().SetMembersAsync(
                userId.HasValue ? BuildUserDirtySetKey(userId.Value) : BuildGlobalDirtySetKey());

            return members
                .Select(member => member.ToString())
                .Where(value => WatchProgressKey.TryParse(value, out _))
                .Select(value =>
                {
                    WatchProgressKey.TryParse(value, out var key);
                    return key;
                })
                .ToArray();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis watch-progress dirty-key scan failed.");
            return [];
        }
    }

    public async Task ClearAsync(WatchProgressKey key, CancellationToken cancellationToken)
    {
        var suffix = key.ToRedisKeySuffix();

        try
        {
            var database = redis.GetDatabase();
            var batch = database.CreateBatch();
            var deleteTask = batch.KeyDeleteAsync(BuildEntryKey(key));
            var globalRemoveTask = batch.SetRemoveAsync(BuildGlobalDirtySetKey(), suffix);
            var userRemoveTask = batch.SetRemoveAsync(BuildUserDirtySetKey(key.UserId), suffix);
            batch.Execute();

            await Task.WhenAll(deleteTask, globalRemoveTask, userRemoveTask);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis watch-progress clear failed for {ProgressKey}.", suffix);
        }
    }

    private static string BuildEntryKey(WatchProgressKey key) => $"sakugavault:watch-progress:{key.ToRedisKeySuffix()}";
    private static string BuildGlobalDirtySetKey() => "sakugavault:watch-progress:dirty";
    private static string BuildUserDirtySetKey(Guid userId) => $"sakugavault:watch-progress:dirty:{userId:D}";
}
