using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SakugaVault.Contracts.Watch;
using SakugaVault.Data;
using SakugaVault.Models;
using SakugaVault.Services.Common;
using SakugaVault.Services.Catalog;

namespace SakugaVault.Services.Metadata;

/// <summary>
/// Consumet-backed metadata sync implementation.
/// It reconciles provider data into the local Anime and Genre tables and invalidates cached catalog output after successful updates.
/// </summary>
public sealed class MetadataSyncService(
    SakugaVaultDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<MetadataSyncService> logger,
    TimeProvider timeProvider) : IMetadataSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<OperationResult<MetadataSyncResultDto>> SyncAnimeMetadataAsync(Guid animeId, CancellationToken cancellationToken)
    {
        var anime = await dbContext.Anime
            .Include(entry => entry.AnimeGenres)
            .FirstOrDefaultAsync(entry => entry.Id == animeId, cancellationToken);
        if (anime is null)
        {
            return OperationResult<MetadataSyncResultDto>.Failure("anime_not_found", "The requested anime could not be found.");
        }

        if (string.IsNullOrWhiteSpace(anime.ExternalMetadataId) || string.IsNullOrWhiteSpace(anime.MetadataProvider))
        {
            return OperationResult<MetadataSyncResultDto>.Failure(
                "external_metadata_missing",
                "This anime needs both ExternalMetadataId and MetadataProvider set before metadata can be synced.");
        }

        var client = httpClientFactory.CreateClient("scraper-client");
        var requestUri =
            $"/anime/{Uri.EscapeDataString(anime.MetadataProvider)}/info?id={Uri.EscapeDataString(anime.ExternalMetadataId)}";

        logger.LogInformation(
            "Starting metadata sync for anime {AnimeId} using provider {Provider} and external ID {ExternalMetadataId}",
            anime.Id,
            anime.MetadataProvider,
            anime.ExternalMetadataId);

        using var response = await client.GetAsync(requestUri, cancellationToken);
        logger.LogInformation(
            "Metadata info request completed for anime {AnimeId} with status {StatusCode}",
            anime.Id,
            (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Metadata sync failed for anime {AnimeId} using provider {Provider}. Status code: {StatusCode}",
                anime.Id,
                anime.MetadataProvider,
                (int)response.StatusCode);

            return OperationResult<MetadataSyncResultDto>.Failure(
                "metadata_sync_failed",
                $"Consumet metadata sync failed with status code {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<ConsumetAnimeInfoResponse>(JsonOptions, cancellationToken);
        if (payload is null)
        {
            return OperationResult<MetadataSyncResultDto>.Failure(
                "metadata_sync_failed",
                "Consumet returned an empty metadata payload.");
        }

        var syncedAt = timeProvider.GetUtcNow();
        anime.Title = SelectValue(payload.Title, anime.Title);
        anime.Synopsis = SelectValue(payload.Description, anime.Synopsis);
        anime.EpisodeCount = payload.TotalEpisodes ?? payload.Episodes?.Count ?? anime.EpisodeCount;
        anime.PosterImageUrl = SelectValue(payload.Image, anime.PosterImageUrl);
        anime.BackdropImageUrl = SelectValue(payload.Cover, payload.Image, anime.BackdropImageUrl);
        anime.SubAvailable = DetermineSubAvailability(payload);
        anime.DubAvailable = DetermineDubAvailability(payload);
        anime.MetadataProvider = anime.MetadataProvider.Trim();
        anime.ExternalMetadataId = anime.ExternalMetadataId.Trim();
        anime.MetadataLastSyncedAtUtc = syncedAt;

        await SyncGenresAsync(anime, payload.Genres, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        cache.Remove(CatalogService.HomeCatalogCacheKey);

        logger.LogInformation(
            "Metadata sync succeeded for anime {AnimeId} using provider {Provider}",
            anime.Id,
            anime.MetadataProvider);

        return OperationResult<MetadataSyncResultDto>.Success(
            new MetadataSyncResultDto(
                anime.Id,
                anime.MetadataProvider,
                syncedAt,
                WasUpdated: true,
                StatusMessage: $"Metadata synced from {anime.MetadataProvider} and catalog cache invalidated."));
    }

    private async Task SyncGenresAsync(Anime anime, IReadOnlyCollection<string>? providerGenres, CancellationToken cancellationToken)
    {
        var normalizedGenres = providerGenres?
            .Where(genre => !string.IsNullOrWhiteSpace(genre))
            .Select(genre => new { Name = genre.Trim(), Slug = Slugify(genre) })
            .DistinctBy(entry => entry.Slug)
            .ToArray() ?? [];

        dbContext.AnimeGenres.RemoveRange(anime.AnimeGenres);
        anime.AnimeGenres.Clear();

        if (normalizedGenres.Length == 0)
        {
            return;
        }

        var slugs = normalizedGenres.Select(entry => entry.Slug).ToArray();
        var existingGenres = await dbContext.Genres
            .Where(genre => slugs.Contains(genre.Slug))
            .ToDictionaryAsync(genre => genre.Slug, cancellationToken);

        foreach (var providerGenre in normalizedGenres)
        {
            if (!existingGenres.TryGetValue(providerGenre.Slug, out var genre))
            {
                genre = new Genre
                {
                    Name = providerGenre.Name,
                    Slug = providerGenre.Slug
                };

                dbContext.Genres.Add(genre);
                existingGenres[providerGenre.Slug] = genre;
            }

            anime.AnimeGenres.Add(new AnimeGenre
            {
                AnimeId = anime.Id,
                Genre = genre
            });
        }
    }

    private static bool DetermineSubAvailability(ConsumetAnimeInfoResponse payload)
    {
        return payload.SubOrDub switch
        {
            "sub" => true,
            "both" => true,
            _ => payload.Subtitles?.Count > 0
        };
    }

    private static bool DetermineDubAvailability(ConsumetAnimeInfoResponse payload)
    {
        return payload.SubOrDub is "dub" or "both";
    }

    private static string SelectValue(string? primary, string fallback)
    {
        return string.IsNullOrWhiteSpace(primary) ? fallback : primary.Trim();
    }

    private static string SelectValue(string? primary, string? secondary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            return secondary.Trim();
        }

        return fallback;
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

    private sealed record ConsumetAnimeInfoResponse(
        string? Title,
        string? Description,
        string? Image,
        string? Cover,
        string? SubOrDub,
        int? TotalEpisodes,
        IReadOnlyCollection<string>? Genres,
        IReadOnlyCollection<ConsumetSubtitleResponse>? Subtitles,
        IReadOnlyCollection<ConsumetEpisodeResponse>? Episodes);

    private sealed record ConsumetEpisodeResponse(
        string? Id,
        double? Number);

    private sealed record ConsumetSubtitleResponse(
        string? Lang,
        string? Url);
}
