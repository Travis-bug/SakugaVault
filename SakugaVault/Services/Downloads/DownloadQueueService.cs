using Microsoft.EntityFrameworkCore;
using SakugaVault.Contracts.Downloads;
using SakugaVault.Data;
using SakugaVault.Models;
using SakugaVault.Services.Common;

namespace SakugaVault.Services.Downloads;

/// <summary>
/// MySQL-backed queue management for planned offline downloads.
/// This gives the downloads screen a real persisted workflow now, while leaving the future worker
/// or file-transfer pipeline as an infrastructure concern.
/// </summary>
public sealed class DownloadQueueService(SakugaVaultDbContext dbContext) : IDownloadQueueService
{
    public async Task<IReadOnlyCollection<DownloadQueueItemDto>> GetUserQueueAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.DownloadRequests
            .AsNoTracking()
            .Where(request => request.UserId == userId)
            .OrderByDescending(request => request.CreatedAtUtc)
            .Select(request => new DownloadQueueItemDto(
                request.Id,
                request.AnimeId,
                request.Anime.Title,
                request.Anime.PosterImageUrl,
                request.EpisodeNumber,
                request.PreferredLanguage,
                request.Quality,
                request.Status,
                request.CreatedAtUtc,
                $"/watch/{request.AnimeId}"))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<OperationResult<DownloadQueueItemDto>> QueueAsync(Guid userId, QueueDownloadRequestDto request, CancellationToken cancellationToken)
    {
        var language = request.PreferredLanguage.Trim().ToLowerInvariant();
        if (language is not ("sub" or "dub"))
        {
            return OperationResult<DownloadQueueItemDto>.Failure("unsupported_language", "PreferredLanguage must be either 'sub' or 'dub'.");
        }

        var anime = await dbContext.Anime
            .AsNoTracking()
            .FirstOrDefaultAsync(anime => anime.Id == request.AnimeId, cancellationToken);

        if (anime is null)
        {
            return OperationResult<DownloadQueueItemDto>.Failure("anime_not_found", "The requested anime could not be found.");
        }

        if (language == "dub" && !anime.DubAvailable)
        {
            return OperationResult<DownloadQueueItemDto>.Failure("language_unavailable", "Dub playback is not available for this title.");
        }

        if (language == "sub" && !anime.SubAvailable)
        {
            return OperationResult<DownloadQueueItemDto>.Failure("language_unavailable", "Sub playback is not available for this title.");
        }

        if (request.EpisodeNumber > anime.EpisodeCount)
        {
            return OperationResult<DownloadQueueItemDto>.Failure("episode_out_of_range", "The requested episode number exceeds the known episode count.");
        }

        var duplicateExists = await dbContext.DownloadRequests
            .AsNoTracking()
            .AnyAsync(
                existing =>
                    existing.UserId == userId &&
                    existing.AnimeId == request.AnimeId &&
                    existing.EpisodeNumber == request.EpisodeNumber &&
                    existing.PreferredLanguage == language,
                cancellationToken);

        if (duplicateExists)
        {
            return OperationResult<DownloadQueueItemDto>.Failure("download_exists", "This episode is already queued for that language.");
        }

        var downloadRequest = new DownloadRequest
        {
            UserId = userId,
            AnimeId = request.AnimeId,
            EpisodeNumber = request.EpisodeNumber,
            PreferredLanguage = language,
            Quality = string.IsNullOrWhiteSpace(request.Quality) ? "1080p" : request.Quality.Trim(),
            Status = "Queued"
        };

        dbContext.DownloadRequests.Add(downloadRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<DownloadQueueItemDto>.Success(
            new DownloadQueueItemDto(
                downloadRequest.Id,
                anime.Id,
                anime.Title,
                anime.PosterImageUrl,
                downloadRequest.EpisodeNumber,
                downloadRequest.PreferredLanguage,
                downloadRequest.Quality,
                downloadRequest.Status,
                downloadRequest.CreatedAtUtc,
                $"/watch/{anime.Id}"));
    }

    public async Task<OperationResult<bool>> RemoveAsync(Guid userId, Guid downloadId, CancellationToken cancellationToken)
    {
        var existingRequest = await dbContext.DownloadRequests
            .FirstOrDefaultAsync(
                request => request.Id == downloadId && request.UserId == userId,
                cancellationToken);

        if (existingRequest is null)
        {
            return OperationResult<bool>.Failure("download_not_found", "The requested download queue entry could not be found.");
        }

        dbContext.DownloadRequests.Remove(existingRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<bool>.Success(true);
    }
}
