using SakugaVault.Contracts.Common;
using SakugaVault.Contracts.Watch;
using SakugaVault.Services.Common;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Service boundary for persisting and retrieving watch progress.
/// This is split out from the page service because watch history is user-state management, not page composition.
/// </summary>
public interface IWatchHistoryService
{
    Task<CursorPagedResult<WatchHistoryEntryDto>> GetUserHistoryAsync(Guid userId, string? cursor, int pageSize, CancellationToken cancellationToken);
    Task<OperationResult<WatchHistoryEntryDto>> UpsertAsync(Guid userId, UpsertWatchHistoryRequestDto request, CancellationToken cancellationToken);
}
