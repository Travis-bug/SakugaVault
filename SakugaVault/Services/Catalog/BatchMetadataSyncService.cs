using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SakugaVault.Contracts.Catalog;
using SakugaVault.Data;
using SakugaVault.Options;
using SakugaVault.Services.Metadata;

namespace SakugaVault.Services.Catalog;

/// <summary>
/// Runs metadata synchronization sequentially to avoid overwhelming upstream providers.
/// </summary>
public sealed class BatchMetadataSyncService(
    SakugaVaultDbContext dbContext,
    IMetadataSyncService metadataSyncService,
    IOptions<ScraperOptions> scraperOptionsAccessor,
    TimeProvider timeProvider) : IBatchMetadataSyncService
{
    private readonly ScraperOptions scraperOptions = scraperOptionsAccessor.Value;

    public async Task<BatchSyncResultDto> BatchSyncAsync(Guid[]? animeIds, CancellationToken cancellationToken)
    {
        var targetIds = animeIds is { Length: > 0 }
            ? await dbContext.Anime
                .AsNoTracking()
                .Where(anime => animeIds.Contains(anime.Id))
                .Select(anime => anime.Id)
                .ToArrayAsync(cancellationToken)
            : await dbContext.Anime
                .AsNoTracking()
                .Where(anime => anime.ExternalMetadataId != null && anime.MetadataProvider != null)
                .Select(anime => anime.Id)
                .ToArrayAsync(cancellationToken);

        var results = new List<Contracts.Watch.MetadataSyncResultDto>(targetIds.Length);
        var succeeded = 0;

        for (var index = 0; index < targetIds.Length; index++)
        {
            var result = await metadataSyncService.SyncAnimeMetadataAsync(targetIds[index], cancellationToken);
            if (result.Succeeded && result.Value is not null)
            {
                results.Add(result.Value);
                succeeded++;
            }
            else
            {
                results.Add(new Contracts.Watch.MetadataSyncResultDto(
                    targetIds[index],
                    Provider: "unknown",
                    SyncedAtUtc: timeProvider.GetUtcNow(),
                    WasUpdated: false,
                    StatusMessage: result.ErrorMessage ?? "Metadata sync failed."));
            }

            if (index < targetIds.Length - 1 && scraperOptions.InterRequestDelayMilliseconds > 0)
            {
                await Task.Delay(scraperOptions.InterRequestDelayMilliseconds, cancellationToken);
            }
        }

        return new BatchSyncResultDto(
            TotalRequested: targetIds.Length,
            Succeeded: succeeded,
            Failed: targetIds.Length - succeeded,
            Results: results);
    }
}
