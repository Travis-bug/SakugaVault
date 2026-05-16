namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Optional request payload for syncing a subset of anime titles.
/// When AnimeIds is null or empty, all syncable titles are processed.
/// </summary>
public sealed record BatchSyncRequestDto(
    Guid[]? AnimeIds);
