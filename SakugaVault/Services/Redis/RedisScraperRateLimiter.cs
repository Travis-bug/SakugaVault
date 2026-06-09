using Microsoft.Extensions.Options;
using SakugaVault.Options;
using StackExchange.Redis;

namespace SakugaVault.Services.Redis;

/// <summary>
/// Redis sliding-window limiter for live scrape triggers.
/// Redis failures fail open so legitimate playback is not blocked by cache infrastructure.
/// </summary>
public sealed class RedisScraperRateLimiter(
    IConnectionMultiplexer redis,
    IOptions<ScraperOptions> scraperOptionsAccessor,
    ILogger<RedisScraperRateLimiter> logger) : IScraperRateLimiter
{
    private const string SlidingWindowScript =
        """
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local window_ms = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local member = ARGV[4]
        local window_start = now - window_ms

        redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)
        local count = redis.call('ZCARD', key)

        if count >= limit then
            local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
            local retry_after = math.ceil(((tonumber(oldest[2]) + window_ms) - now) / 1000)
            if retry_after < 1 then retry_after = 1 end
            return {0, 0, retry_after}
        end

        redis.call('ZADD', key, now, member)
        redis.call('PEXPIRE', key, window_ms + 1000)
        return {1, limit - count - 1, 0}
        """;

    private readonly ScraperRateLimitOptions options = scraperOptionsAccessor.Value.RateLimit;

    public async Task<ScraperRateLimitResult> CheckAsync(string partitionKey, CancellationToken cancellationToken)
    {
        var window = TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds));
        var limit = Math.Max(1, options.PermitLimit);
        var key = $"sakugavault:scrape:ratelimit:{partitionKey}";

        try
        {
            var result = (RedisResult[])(await redis.GetDatabase().ScriptEvaluateAsync(
                SlidingWindowScript,
                new RedisKey[] { key },
                new RedisValue[]
                {
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    (long)window.TotalMilliseconds,
                    limit,
                    Guid.NewGuid().ToString("N")
                }))!;

            var allowed = (int)result[0] == 1;
            var remaining = (int)result[1];
            var retryAfter = TimeSpan.FromSeconds((int)result[2]);
            return new ScraperRateLimitResult(allowed, remaining, retryAfter);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Scraper rate-limit check failed for {PartitionKey}; allowing live scrape.", partitionKey);
            return new ScraperRateLimitResult(true, limit, TimeSpan.Zero);
        }
    }
}
