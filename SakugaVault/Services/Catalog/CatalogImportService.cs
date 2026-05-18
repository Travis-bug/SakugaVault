using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SakugaVault.Contracts.Catalog;
using SakugaVault.Data;
using SakugaVault.Models;
using SakugaVault.Services.Common;
using SakugaVault.Services.Metadata;

namespace SakugaVault.Services.Catalog;

/// <summary>
/// Provider-backed catalog importer.
/// It pulls titles from a live upstream feed, upserts them into MySQL, and optionally chains a metadata sync pass.
/// </summary>
public sealed class CatalogImportService(
    SakugaVaultDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IMetadataSyncService metadataSyncService,
    ILogger<CatalogImportService> logger) : ICatalogImportService
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true
    };

    public async Task<OperationResult<CatalogImportResultDto>> ImportFromProviderAsync(
        ImportCatalogRequestDto request,
        CancellationToken cancellationToken)
    {
        var provider = request.Provider.Trim();
        var feed = request.Feed.Trim();
        var client = httpClientFactory.CreateClient("scraper-client");
        var importedTitles = new List<(Anime Anime, bool Created)>();

        logger.LogInformation(
            "Starting catalog import from provider {Provider}, feed {Feed}, pages {PageCount}, sync metadata {SyncMetadata}",
            provider,
            feed,
            request.PageCount,
            request.SyncMetadata);

        for (var page = 1; page <= request.PageCount; page++)
        {
            var requestUri = $"/anime/{Uri.EscapeDataString(provider)}/{Uri.EscapeDataString(feed)}?page={page}";
            using var response = await client.GetAsync(requestUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<CatalogImportResultDto>.Failure(
                    "catalog_import_failed",
                    $"Catalog import feed {feed} failed with status code {(int)response.StatusCode} from provider {provider}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return OperationResult<CatalogImportResultDto>.Failure(
                    "catalog_import_failed",
                    $"Catalog import feed {feed} returned an empty body.");
            }

            JsonDocument payload;
            try
            {
                payload = JsonDocument.Parse(body, JsonOptions);
            }
            catch (JsonException)
            {
                return OperationResult<CatalogImportResultDto>.Failure(
                    "catalog_import_failed",
                    "The configured scraper base URL did not return JSON. Point Scrapers:ConsumetBaseUrl at a working self-hosted provider API before importing.");
            }

            using (payload)
            {
                var feedItems = ReadFeedItems(payload.RootElement);
                if (feedItems.Count == 0)
                {
                    logger.LogInformation(
                        "Catalog import page {Page} from provider {Provider} returned no feed items.",
                        page,
                        provider);

                    continue;
                }

                var importedPage = await UpsertFeedPageAsync(provider, feed, feedItems, importedTitles.Count, cancellationToken);
                importedTitles.AddRange(importedPage);
            }
        }

        if (importedTitles.Count == 0)
        {
            return OperationResult<CatalogImportResultDto>.Failure(
                "catalog_import_failed",
                $"No importable titles were returned from provider {provider} feed {feed}.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var metadataSyncedIds = new HashSet<Guid>();
        if (request.SyncMetadata)
        {
            foreach (var importedTitle in importedTitles)
            {
                var syncResult = await metadataSyncService.SyncAnimeMetadataAsync(importedTitle.Anime.Id, cancellationToken);
                if (syncResult.Succeeded)
                {
                    metadataSyncedIds.Add(importedTitle.Anime.Id);
                }
            }
        }

        cache.Remove(CatalogService.HomeCatalogCacheKey);

        var createdCount = importedTitles.Count(entry => entry.Created);
        var result = new CatalogImportResultDto(
            provider,
            feed,
            request.PageCount,
            importedTitles.Count,
            createdCount,
            importedTitles.Count - createdCount,
            metadataSyncedIds.Count,
            importedTitles
                .Select(entry => new ImportedCatalogTitleDto(
                    entry.Anime.Id,
                    entry.Anime.Title,
                    entry.Anime.ExternalMetadataId ?? string.Empty,
                    entry.Anime.PosterImageUrl,
                    entry.Created,
                    metadataSyncedIds.Contains(entry.Anime.Id)))
                .ToArray());

        logger.LogInformation(
            "Catalog import completed for provider {Provider}, feed {Feed}. Imported {ImportedCount} titles.",
            provider,
            feed,
            importedTitles.Count);

        return OperationResult<CatalogImportResultDto>.Success(result);
    }

    private async Task<IReadOnlyCollection<(Anime Anime, bool Created)>> UpsertFeedPageAsync(
        string provider,
        string feed,
        IReadOnlyCollection<ProviderFeedItem> feedItems,
        int existingImportOffset,
        CancellationToken cancellationToken)
    {
        var externalIds = feedItems.Select(item => item.ExternalMetadataId).ToArray();
        var existingByExternalId = await dbContext.Anime
            .Where(anime => anime.MetadataProvider == provider && anime.ExternalMetadataId != null && externalIds.Contains(anime.ExternalMetadataId))
            .ToDictionaryAsync(anime => anime.ExternalMetadataId!, cancellationToken);

        var existingSlugs = new HashSet<string>(
            await dbContext.Anime
                .Select(anime => anime.Slug)
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        var touchedTitles = new List<(Anime Anime, bool Created)>(feedItems.Count);
        var importRank = existingImportOffset + 1;

        foreach (var feedItem in feedItems)
        {
            var created = !existingByExternalId.TryGetValue(feedItem.ExternalMetadataId, out var existingAnime) || existingAnime is null;
            Anime anime;
            if (created)
            {
                var slug = BuildUniqueSlug(feedItem, existingSlugs);
                anime = new Anime
                {
                    Slug = slug,
                    RuntimeMinutes = 24
                };

                dbContext.Anime.Add(anime);
                existingByExternalId[feedItem.ExternalMetadataId] = anime;
            }
            else
            {
                anime = existingAnime!;
            }

            anime.Title = feedItem.Title;
            anime.Synopsis = string.IsNullOrWhiteSpace(anime.Synopsis)
                ? $"Imported from the {provider} {feed} feed. Open the watch page to hydrate live metadata."
                : anime.Synopsis;
            anime.PosterImageUrl = feedItem.ImageUrl;
            anime.BackdropImageUrl = string.IsNullOrWhiteSpace(anime.BackdropImageUrl)
                ? feedItem.ImageUrl
                : anime.BackdropImageUrl;
            anime.EpisodeCount = feedItem.EpisodeCount > 0
                ? feedItem.EpisodeCount
                : Math.Max(anime.EpisodeCount, 1);
            anime.SubAvailable = DetermineSubAvailability(feedItem.SubOrDub, anime.SubAvailable);
            anime.DubAvailable = DetermineDubAvailability(feedItem.SubOrDub, anime.DubAvailable);
            anime.TrendingRank = importRank++;
            anime.MetadataProvider = provider;
            anime.ExternalMetadataId = feedItem.ExternalMetadataId;

            touchedTitles.Add((anime, created));
        }

        return touchedTitles;
    }

    private static List<ProviderFeedItem> ReadFeedItems(JsonElement root)
    {
        var items = new List<ProviderFeedItem>();
        var source = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
                ? results
                : default;

        if (source.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var item in source.EnumerateArray())
        {
            var externalId = ReadString(item, "id");
            var imageUrl = ReadString(item, "image");
            var title = ReadTitle(item);

            if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(imageUrl) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            items.Add(new ProviderFeedItem(
                externalId.Trim(),
                title.Trim(),
                imageUrl.Trim(),
                ReadEpisodeCount(item),
                ReadString(item, "subOrDub")));
        }

        return items;
    }

    private static string BuildUniqueSlug(ProviderFeedItem feedItem, ISet<string> existingSlugs)
    {
        var baseSlug = Slugify(feedItem.Title);
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            baseSlug = Slugify(feedItem.ExternalMetadataId);
        }

        var slug = baseSlug;
        var counter = 2;
        while (!existingSlugs.Add(slug))
        {
            slug = $"{baseSlug}-{counter++}";
        }

        return slug;
    }

    private static int ReadEpisodeCount(JsonElement item)
    {
        if (TryReadInt(item, "episodesCount", out var episodesCount))
        {
            return episodesCount;
        }

        if (TryReadInt(item, "totalEpisodes", out var totalEpisodes))
        {
            return totalEpisodes;
        }

        if (TryReadInt(item, "episodeNumber", out var episodeNumber))
        {
            return episodeNumber;
        }

        return 0;
    }

    private static string ReadTitle(JsonElement item)
    {
        if (!item.TryGetProperty("title", out var titleNode))
        {
            return string.Empty;
        }

        if (titleNode.ValueKind == JsonValueKind.String)
        {
            return titleNode.GetString() ?? string.Empty;
        }

        if (titleNode.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return ReadString(titleNode, "english")
            ?? ReadString(titleNode, "romaji")
            ?? ReadString(titleNode, "native")
            ?? ReadString(titleNode, "userPreferred")
            ?? string.Empty;
    }

    private static string? ReadString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryReadInt(JsonElement item, string propertyName, out int value)
    {
        value = 0;
        if (!item.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetInt32(out value);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return int.TryParse(property.GetString(), out value);
        }

        return false;
    }

    private static bool DetermineSubAvailability(string? subOrDub, bool fallback)
    {
        return subOrDub?.Trim().ToLowerInvariant() switch
        {
            "sub" => true,
            "both" => true,
            "dub" => false,
            _ => fallback
        };
    }

    private static bool DetermineDubAvailability(string? subOrDub, bool fallback)
    {
        return subOrDub?.Trim().ToLowerInvariant() switch
        {
            "dub" => true,
            "both" => true,
            "sub" => false,
            _ => fallback
        };
    }

    private static string Slugify(string value)
    {
        var slug = value.Trim().ToLowerInvariant();
        var builder = new StringBuilder(slug.Length);
        var previousWasSeparator = false;

        foreach (var character in slug)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private sealed record ProviderFeedItem(
        string ExternalMetadataId,
        string Title,
        string ImageUrl,
        int EpisodeCount,
        string? SubOrDub);
}
