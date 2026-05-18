using SakugaVault.Contracts.Auth;
using SakugaVault.Contracts.Downloads;
using SakugaVault.Contracts.Watch;

namespace SakugaVault.Contracts.Profile;

/// <summary>
/// Aggregated payload for the profile screen.
/// It combines identity, usage stats, recent watch history, and queue preview so React can render
/// the page with one request.
/// </summary>
public sealed record ProfileSummaryDto(
    CurrentUserDto User,
    int ContinueWatchingCount,
    int CompletedEntriesCount,
    int CommentsCount,
    int QueuedDownloadsCount,
    IReadOnlyCollection<WatchHistoryEntryDto> RecentHistory,
    IReadOnlyCollection<DownloadQueueItemDto> DownloadQueuePreview);
