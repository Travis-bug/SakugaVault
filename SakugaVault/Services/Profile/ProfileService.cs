using Microsoft.EntityFrameworkCore;
using SakugaVault.Contracts.Profile;
using SakugaVault.Data;
using SakugaVault.Services.Auth;
using SakugaVault.Services.Downloads;
using SakugaVault.Services.Watch;

namespace SakugaVault.Services.Profile;

/// <summary>
/// Aggregates identity, watch history, comment totals, and download-queue preview for the profile UI.
/// This keeps cross-service page composition in one place and preserves the thin-controller rule.
/// </summary>
public sealed class ProfileService(
    IAuthService authService,
    IWatchHistoryService watchHistoryService,
    IDownloadQueueService downloadQueueService,
    SakugaVaultDbContext dbContext) : IProfileService
{
    public async Task<ProfileSummaryDto?> GetCurrentProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await authService.GetCurrentUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        // This service shares one scoped DbContext with the downstream services. Running these queries in parallel
        // triggers EF Core's "second operation was started" guard, so the profile aggregate is composed sequentially.
        var recentHistory = await watchHistoryService.GetUserHistoryAsync(userId, cursor: null, pageSize: 8, cancellationToken);
        var downloadQueue = await downloadQueueService.GetUserQueueAsync(userId, cancellationToken);
        var continueWatchingCount = await dbContext.WatchHistoryEntries
            .AsNoTracking()
            .CountAsync(entry => entry.UserId == userId && !entry.Completed, cancellationToken);
        var completedEntriesCount = await dbContext.WatchHistoryEntries
            .AsNoTracking()
            .CountAsync(entry => entry.UserId == userId && entry.Completed, cancellationToken);
        var commentsCount = await dbContext.AnimeComments
            .AsNoTracking()
            .CountAsync(comment => comment.UserId == userId, cancellationToken);

        return new ProfileSummaryDto(
            user,
            continueWatchingCount,
            completedEntriesCount,
            commentsCount,
            downloadQueue.Count,
            recentHistory.Items,
            downloadQueue.Take(6).ToArray());
    }
}
