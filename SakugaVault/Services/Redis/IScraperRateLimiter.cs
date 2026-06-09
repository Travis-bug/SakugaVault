namespace SakugaVault.Services.Redis;

public interface IScraperRateLimiter
{
    Task<ScraperRateLimitResult> CheckAsync(string partitionKey, CancellationToken cancellationToken);
}

public sealed record ScraperRateLimitResult(
    bool Allowed,
    int Remaining,
    TimeSpan RetryAfter);
