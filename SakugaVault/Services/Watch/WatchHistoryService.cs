using System.Text;
using SakugaVault.Contracts.Common;
using Microsoft.EntityFrameworkCore;
using SakugaVault.Contracts.Watch;
using SakugaVault.Data;
using SakugaVault.Models;
using SakugaVault.Services.Common;

namespace SakugaVault.Services.Watch;

/// <summary>
/// MySQL-backed watch-history service.
/// This is the persistence layer for resume playback and continue-watching UX.
/// </summary>
public sealed class WatchHistoryService(
    SakugaVaultDbContext dbContext,
    TimeProvider timeProvider) : IWatchHistoryService
{
    public async Task<CursorPagedResult<WatchHistoryEntryDto>> GetUserHistoryAsync(
        Guid userId,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.WatchHistoryEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId);

        if (TryDecodeCursor(cursor, out var lastWatchedAtUtc, out var entryId))
        {
            query = query.Where(entry =>
                entry.LastWatchedAtUtc < lastWatchedAtUtc ||
                (entry.LastWatchedAtUtc == lastWatchedAtUtc && entry.Id.CompareTo(entryId) < 0));
        }

        var rows = await query
            .OrderByDescending(entry => entry.LastWatchedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new HistoryRow(
                entry.Id,
                new WatchHistoryEntryDto(
                    entry.AnimeId,
                    entry.Anime.Title,
                    entry.Anime.PosterImageUrl,
                    entry.EpisodeNumber,
                    entry.PositionSeconds,
                    entry.DurationSeconds,
                    entry.Completed,
                    entry.LastWatchedAtUtc),
                entry.LastWatchedAtUtc))
            .Take(normalizedPageSize + 1)
            .ToArrayAsync(cancellationToken);

        var hasMore = rows.Length > normalizedPageSize;
        var pageRows = hasMore ? rows.Take(normalizedPageSize).ToArray() : rows;
        var nextCursor = hasMore ? EncodeCursor(pageRows[^1].LastWatchedAtUtc, pageRows[^1].EntryId) : null;

        return new CursorPagedResult<WatchHistoryEntryDto>(
            pageRows.Select(row => row.Item).ToArray(),
            nextCursor,
            normalizedPageSize,
            hasMore);
    }

    public async Task<OperationResult<WatchHistoryEntryDto>> UpsertAsync(Guid userId, UpsertWatchHistoryRequestDto request, CancellationToken cancellationToken)
    {
        var historyContext = await dbContext.Anime
            .Where(anime => anime.Id == request.AnimeId)
            .Select(anime => new
            {
                anime.Id,
                anime.Title,
                anime.PosterImageUrl,
                ExistingEntry = anime.WatchHistoryEntries
                    .FirstOrDefault(entry =>
                        entry.UserId == userId &&
                        entry.EpisodeNumber == request.EpisodeNumber)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (historyContext is null)
        {
            return OperationResult<WatchHistoryEntryDto>.Failure("anime_not_found", "The requested anime could not be found.");
        }

        var existingEntry = historyContext.ExistingEntry;

        if (existingEntry is null)
        {
            existingEntry = new WatchHistoryEntry
            {
                AnimeId = request.AnimeId,
                UserId = userId,
                EpisodeNumber = request.EpisodeNumber
            };

            dbContext.WatchHistoryEntries.Add(existingEntry);
        }

        existingEntry.PositionSeconds = request.PositionSeconds;
        existingEntry.DurationSeconds = request.DurationSeconds;
        existingEntry.Completed = request.Completed;
        existingEntry.LastWatchedAtUtc = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<WatchHistoryEntryDto>.Success(
            new WatchHistoryEntryDto(
                historyContext.Id,
                historyContext.Title,
                historyContext.PosterImageUrl,
                existingEntry.EpisodeNumber,
                existingEntry.PositionSeconds,
                existingEntry.DurationSeconds,
                existingEntry.Completed,
                existingEntry.LastWatchedAtUtc));
    }

    private static string? EncodeCursor(DateTimeOffset lastWatchedAtUtc, Guid entryId)
    {
        var rawCursor = $"{lastWatchedAtUtc.UtcDateTime.Ticks}:{entryId:D}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCursor));
    }

    private static bool TryDecodeCursor(string? cursor, out DateTimeOffset lastWatchedAtUtc, out Guid entryId)
    {
        lastWatchedAtUtc = default;
        entryId = default;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var rawCursor = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = rawCursor.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], out var ticks) ||
                !Guid.TryParse(parts[1], out entryId))
            {
                return false;
            }

            lastWatchedAtUtc = new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record HistoryRow(
        Guid EntryId,
        WatchHistoryEntryDto Item,
        DateTimeOffset LastWatchedAtUtc);
}
