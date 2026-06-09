namespace SakugaVault.Options;

/// <summary>
/// Redis connection settings shared by cache, rate limiter, playback proxy, and watch-progress buffering.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; init; } = "localhost:6379";
}
