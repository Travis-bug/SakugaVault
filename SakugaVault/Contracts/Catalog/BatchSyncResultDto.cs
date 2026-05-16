using SakugaVault.Contracts.Watch;

namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Aggregate result for an admin-triggered batch metadata sync.
/// </summary>
public sealed record BatchSyncResultDto(
    int TotalRequested,
    int Succeeded,
    int Failed,
    IReadOnlyCollection<MetadataSyncResultDto> Results);
