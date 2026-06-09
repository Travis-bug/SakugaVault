namespace SakugaVault.Services.Watch;

public interface IPlaybackStreamRegistry
{
    bool TryRegister(Guid streamId, ProxiedPlaybackStream stream, TimeSpan ttl);
    bool TryGet(Guid streamId, out ProxiedPlaybackStream? stream);
}

public sealed record ProxiedPlaybackStream(
    string Url,
    Dictionary<string, string> Headers,
    Guid OwnerUserId,
    string PlaybackSessionId,
    DateTimeOffset CreatedAtUtc);
