using SakugaVault.Contracts.Watch;
using SakugaVault.Services.Common;

namespace SakugaVault.Services.Metadata;

/// <summary>
/// Boundary for syncing local anime metadata with external sources.
/// This is intentionally separate from the watch-page service because metadata refresh is an application workflow, not a page read operation.
/// </summary>
public interface IMetadataSyncService
{
    Task<OperationResult<MetadataSyncResultDto>> SyncAnimeMetadataAsync(Guid animeId, CancellationToken cancellationToken);
}
