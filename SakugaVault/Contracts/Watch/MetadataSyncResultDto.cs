namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Response from a metadata synchronization attempt.
/// This gives the client and the developer visibility into when a title was refreshed and what provider handled it.
/// </summary>
public sealed record MetadataSyncResultDto(
    Guid AnimeId,
    string Provider,
    DateTimeOffset SyncedAtUtc,
    bool WasUpdated,
    string StatusMessage);
