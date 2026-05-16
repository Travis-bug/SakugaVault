using SakugaVault.Contracts.Catalog;

namespace SakugaVault.Services.Catalog;

/// <summary>
/// Coordinates sequential metadata synchronization across multiple anime titles.
/// </summary>
public interface IBatchMetadataSyncService
{
    Task<BatchSyncResultDto> BatchSyncAsync(Guid[]? animeIds, CancellationToken cancellationToken);
}
