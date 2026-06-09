using Microsoft.Extensions.Options;
using SakugaVault.Options;
using StackExchange.Redis;

namespace SakugaVault.Services.Redis;

/// <summary>
/// Performs a non-fatal Redis reachability check during startup.
/// Cache and rate-limit features fail open where safe, but operators still need a clear warning.
/// </summary>
public sealed class RedisStartupCheckService(
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> redisOptionsAccessor,
    ILogger<RedisStartupCheckService> logger) : IHostedService
{
    private readonly RedisOptions redisOptions = redisOptionsAccessor.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            logger.LogInformation("Redis is reachable at {ConnectionString}.", redisOptions.ConnectionString);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "WARNING: Redis is not reachable at {ConnectionString}. Redis-backed cache/rate/progress features will fail open where safe.",
                redisOptions.ConnectionString);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
