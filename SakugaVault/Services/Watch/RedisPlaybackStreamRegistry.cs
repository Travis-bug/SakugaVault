using System.Text.Json;
using StackExchange.Redis;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Stores short-lived proxy stream registrations in Redis so they work across API instances.
/// Registration failures are fail-closed because returning an anonymous dead URL would bypass replay protection.
/// </summary>
public sealed class RedisPlaybackStreamRegistry(
    IConnectionMultiplexer redis,
    ILogger<RedisPlaybackStreamRegistry> logger) : IPlaybackStreamRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool TryRegister(Guid streamId, ProxiedPlaybackStream stream, TimeSpan ttl)
    {
        try
        {
            var json = JsonSerializer.Serialize(stream, JsonOptions);
            return redis.GetDatabase().StringSet(BuildKey(streamId), json, ttl);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to register playback stream {StreamId} in Redis.", streamId);
            return false;
        }
    }

    public bool TryGet(Guid streamId, out ProxiedPlaybackStream? stream)
    {
        stream = null;

        try
        {
            var value = redis.GetDatabase().StringGet(BuildKey(streamId));
            if (!value.HasValue)
            {
                return false;
            }

            stream = JsonSerializer.Deserialize<ProxiedPlaybackStream>(value.ToString(), JsonOptions);
            return stream is not null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to read playback stream {StreamId} from Redis.", streamId);
            return false;
        }
    }

    private static string BuildKey(Guid streamId) => $"sakugavault:playback:stream:{streamId:D}";
}
