using System.Text.Json;
using Microsoft.Extensions.Options;
using SakugaVault.Options;
using SakugaVault.Services.Scraping;
using StackExchange.Redis;

namespace SakugaVault.Services.Redis;

/// <summary>
/// Redis-backed cache for successful playback resolutions.
/// Cache failures are deliberately non-fatal: a miss falls through to the live resolver.
/// </summary>
public sealed class StreamCacheService(
    IConnectionMultiplexer redis,
    IOptions<ScraperOptions> scraperOptionsAccessor,
    ILogger<StreamCacheService> logger) : IStreamCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan ttl = TimeSpan.FromMinutes(
        Math.Max(1, scraperOptionsAccessor.Value.StreamCacheTtlMinutes));

    public async Task<StreamScrapeResult?> GetAsync(StreamCacheKey key, CancellationToken cancellationToken)
    {
        var redisKey = BuildKey(key);
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(redisKey);
            if (!value.HasValue)
            {
                return null;
            }

            var cached = JsonSerializer.Deserialize<CachedStreamScrapeResult>(value.ToString(), JsonOptions);
            return cached?.ToResult();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Stream cache lookup failed for {CacheKey}; falling through to live scrape.", redisKey);
            return null;
        }
    }

    public async Task SetAsync(StreamCacheKey key, StreamScrapeResult result, CancellationToken cancellationToken)
    {
        if (!result.IsResolved || string.IsNullOrWhiteSpace(result.StreamUrl))
        {
            return;
        }

        var redisKey = BuildKey(key);
        try
        {
            var json = JsonSerializer.Serialize(CachedStreamScrapeResult.From(result), JsonOptions);
            await redis.GetDatabase().StringSetAsync(redisKey, json, ttl);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Stream cache write failed for {CacheKey}; playback will continue without caching.", redisKey);
        }
    }

    public async Task InvalidateAsync(Guid animeId, CancellationToken cancellationToken)
    {
        try
        {
            var server = redis.GetServers().FirstOrDefault();
            if (server is null)
            {
                return;
            }

            var keys = server.Keys(pattern: $"sakugavault:stream:cache:{animeId:D}:*").ToArray();
            if (keys.Length == 0)
            {
                return;
            }

            await redis.GetDatabase().KeyDeleteAsync(keys);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Stream cache invalidation failed for anime {AnimeId}.", animeId);
        }
    }

    private static RedisKey BuildKey(StreamCacheKey key)
    {
        return $"sakugavault:stream:cache:{key.ToRedisKeySuffix()}";
    }

    private sealed record CachedStreamScrapeResult(
        bool IsResolved,
        string PreferredProtocol,
        string? StreamUrl,
        string? SourceHost,
        string Provider,
        string StatusMessage,
        string Resolver,
        int ResolverPriority,
        string? AudioLanguage,
        string? SubtitleLanguage,
        string? LanguageWarning,
        Dictionary<string, string> SourceRequestHeaders,
        List<StreamSubtitleTrack> SubtitleTracks)
    {
        public static CachedStreamScrapeResult From(StreamScrapeResult result) =>
            new(
                result.IsResolved,
                result.PreferredProtocol,
                result.StreamUrl,
                result.SourceHost,
                result.Provider,
                result.StatusMessage,
                result.Resolver,
                result.ResolverPriority,
                result.AudioLanguage,
                result.SubtitleLanguage,
                result.LanguageWarning,
                new Dictionary<string, string>(result.SourceRequestHeaders, StringComparer.OrdinalIgnoreCase),
                result.SubtitleTracks.ToList());

        public StreamScrapeResult ToResult() =>
            new(IsResolved, PreferredProtocol, StreamUrl, SourceHost, Provider, StatusMessage)
            {
                Resolver = Resolver,
                ResolverPriority = ResolverPriority,
                AudioLanguage = AudioLanguage,
                SubtitleLanguage = SubtitleLanguage,
                LanguageWarning = LanguageWarning,
                SourceRequestHeaders = new Dictionary<string, string>(SourceRequestHeaders, StringComparer.OrdinalIgnoreCase),
                SubtitleTracks = SubtitleTracks
            };
    }
}
