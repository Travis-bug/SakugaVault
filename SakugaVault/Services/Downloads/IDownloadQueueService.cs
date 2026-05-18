using SakugaVault.Contracts.Downloads;
using SakugaVault.Services.Common;

namespace SakugaVault.Services.Downloads;

/// <summary>
/// Service boundary for persisted download-queue workflows.
/// The controller only routes requests and applies auth; queue validation and duplicate handling
/// live here.
/// </summary>
public interface IDownloadQueueService
{
    Task<IReadOnlyCollection<DownloadQueueItemDto>> GetUserQueueAsync(Guid userId, CancellationToken cancellationToken);
    Task<OperationResult<DownloadQueueItemDto>> QueueAsync(Guid userId, QueueDownloadRequestDto request, CancellationToken cancellationToken);
    Task<OperationResult<bool>> RemoveAsync(Guid userId, Guid downloadId, CancellationToken cancellationToken);
}
