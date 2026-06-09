using StackExchange.Redis;

namespace SakugaVault.Services.Redis;

/// <summary>
/// Collapses identical live scrape misses so one in-flight request fills Redis while duplicates wait.
/// Redis failures fail open so playback does not depend on the lock layer being available.
/// </summary>
public sealed class RedisStreamResolutionLockService(
    IConnectionMultiplexer redis,
    ILogger<RedisStreamResolutionLockService> logger) : IStreamResolutionLockService
{
    private const string ReleaseScript =
        """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end

        return 0
        """;

    public async Task<IAsyncDisposable?> TryAcquireAsync(StreamCacheKey key, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var lockKey = BuildLockKey(key);
        var token = Guid.NewGuid().ToString("N");

        try
        {
            var acquired = await redis.GetDatabase().StringSetAsync(lockKey, token, lifetime, When.NotExists);
            return acquired
                ? new RedisStreamResolutionLock(redis, lockKey, token, logger)
                : null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Scrape stampede lock failed for {LockKey}; allowing live scrape.", lockKey);
            return NoopStreamResolutionLock.Instance;
        }
    }

    private static RedisKey BuildLockKey(StreamCacheKey key) => $"sakugavault:scrape:lock:{key.ToRedisKeySuffix()}";

    private sealed class RedisStreamResolutionLock(
        IConnectionMultiplexer redis,
        RedisKey lockKey,
        string token,
        ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await redis.GetDatabase().ScriptEvaluateAsync(
                    ReleaseScript,
                    new RedisKey[] { lockKey },
                    new RedisValue[] { token });
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to release scrape stampede lock {LockKey}.", lockKey);
            }
        }
    }

    private sealed class NoopStreamResolutionLock : IAsyncDisposable
    {
        public static NoopStreamResolutionLock Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
