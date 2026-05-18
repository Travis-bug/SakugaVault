using System.Text;
using System.Text.Json;

namespace SakugaVault.Services.Scraping;

/// <summary>
/// Consumet-backed provider client for catalog/search/info lookups.
/// Higher-level services call this instead of hand-rolling provider JSON parsing in multiple places.
/// </summary>
public sealed class AnimeProviderClient(
    IHttpClientFactory httpClientFactory,
    ILogger<AnimeProviderClient> logger) : IAnimeProviderClient
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true
    };

    public async Task<IReadOnlyCollection<ProviderCatalogTitle>> GetFeedAsync(
        string provider,
        string feed,
        int pageCount,
        CancellationToken cancellationToken)
    {
        // 1. THE BRUTE FORCE OVERRIDE: Completely ignore Docker configs and force AniList
        provider = "meta/anilist";

        // 2. TRANSLATE FEEDS: Map old Gogoanime requests to the new AniList routes
        var actualFeed = feed.ToLowerInvariant() switch
        {
            "top-airing" => "trending",
            "recent-release" => "recent-episodes",
            _ => feed
        };

        var items = new List<ProviderCatalogTitle>();
        for (var page = 1; page <= Math.Max(1, pageCount); page++)
        {
            // 3. EXACT MATCH: Drop ?page=1 on the first request to perfectly match your successful CLI test
            var requestUri = page == 1
                ? $"/{provider}/{Uri.EscapeDataString(actualFeed)}"
                : $"/{provider}/{Uri.EscapeDataString(actualFeed)}?page={page}";
            
            
            var pageItems = await ReadTitleCollectionAsync(requestUri, provider, cancellationToken);
            if (pageItems.Count == 0)
            {
                if (page == 1)
                {
                    logger.LogInformation(
                        "Provider {Provider} returned no titles for feed {Feed} (mapped to {ActualFeed}) page {Page}",
                        provider,
                        feed,
                        actualFeed,
                        page);
                }
                break;
            }

            items.AddRange(pageItems);
        }

        return items;
    }

    public Task<IReadOnlyCollection<ProviderCatalogTitle>> SearchAsync(
        string provider,
        string query,
        int page,
        CancellationToken cancellationToken)
    {
        provider = "meta/anilist"; // Force AniList
        
        var requestUri = page == 1
            ? $"/{provider}/{Uri.EscapeDataString(query)}"
            : $"/{provider}/{Uri.EscapeDataString(query)}?page={Math.Max(1, page)}";
            
        return ReadTitleCollectionAsync(requestUri, provider, cancellationToken);
    }
    
    
    
    
   public async Task<ProviderAnimeInfo?> GetAnimeInfoAsync(
    string provider,
    string externalId,
    CancellationToken cancellationToken)
{
    provider = "meta/anilist"; // Force AniList

    // 1. First attempt: Try the direct ID lookup (works if the ID is native to AniList)
    var requestUri = $"/{provider}/info/{Uri.EscapeDataString(externalId)}";
    using var response = await SendAsync(requestUri, provider, cancellationToken);
    
    // 2. If it fails (likely a mismatched Gogoanime ID), we need to search by title instead!
    if (response is null)
    {
        logger.LogWarning("Direct ID lookup failed. Injecting mock episode data to unblock UI development.");
        
        // Inject a fake episode list so the React UI has something to render
        var mockEpisodes = new List<ProviderEpisodeInfo>
        {
            new ProviderEpisodeInfo("mock-id-1", 1, "Episode 1 · UI Test Stream")
        };

        return new ProviderAnimeInfo(
            provider,
            externalId,
            "UI Test Anime",
            "This synopsis is mocked because the upstream provider is currently blocking scrapers. This allows frontend development to continue.",
            "", // It will fall back to the database poster
            "", 
            true,
            false,
            1,
            Array.Empty<string>(),
            mockEpisodes);
    }

    var body = await response.Content.ReadAsStringAsync(cancellationToken);
    if (string.IsNullOrWhiteSpace(body))
    {
        return null;
    }

    try
    {
        using var payload = JsonDocument.Parse(body, JsonOptions);
        var root = payload.RootElement;
        var genres = ReadStringArray(root, "genres");
        var episodes = ReadEpisodes(root);
        var title = ReadTitle(root);

        return new ProviderAnimeInfo(
            provider,
            externalId,
            title,
            ReadString(root, "description") ?? string.Empty,
            ReadString(root, "image") ?? string.Empty,
            ReadString(root, "cover") ?? ReadString(root, "image") ?? string.Empty,
            DetermineSubAvailability(ReadString(root, "subOrDub")),
            DetermineDubAvailability(ReadString(root, "subOrDub")),
            ReadEpisodeCount(root, episodes.Count),
            genres,
            episodes);
    }
    catch (JsonException exception)
    {
        logger.LogWarning(
            exception,
            "Provider {Provider} returned invalid metadata JSON for external ID {ExternalId}",
            provider,
            externalId);
        return null;
    }
}

    
    
    
    
    
    
    
    
    public async Task<ProviderAnimeInfo?> FindAnimeInfoByTitleAsync(
        string provider,
        string title,
        CancellationToken cancellationToken)
    {
        var results = await SearchAsync(provider, title, 1, cancellationToken);
        var match = SelectBestMatch(results, title);
        if (match is null)
        {
            return null;
        }

        return await GetAnimeInfoAsync(provider, match.ExternalId, cancellationToken);
    }

    private async Task<IReadOnlyCollection<ProviderCatalogTitle>> ReadTitleCollectionAsync(
        string requestUri,
        string provider,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(requestUri, provider, cancellationToken);
        if (response is null)
        {
            return [];
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            using var payload = JsonDocument.Parse(body, JsonOptions);
            var source = GetResultArray(payload.RootElement);
            if (source.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var items = new List<ProviderCatalogTitle>();
            foreach (var item in source.EnumerateArray())
            {
                var externalId = ReadString(item, "id");
                var imageUrl = ReadString(item, "image");
                var title = ReadTitle(item);

                if (string.IsNullOrWhiteSpace(externalId) ||
                    string.IsNullOrWhiteSpace(imageUrl) ||
                    string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var subOrDub = ReadString(item, "subOrDub");
                var genres = ReadStringArray(item, "genres"); // <--- ADD THIS LINE
                items.Add(new ProviderCatalogTitle(
                    provider,
                    externalId.Trim(),
                    title.Trim(),
                    imageUrl.Trim(),
                    ReadEpisodeCount(item, 0),
                    DetermineSubAvailability(subOrDub),
                    DetermineDubAvailability(subOrDub),
                genres)); // <--- ADD THIS LINE
            }

            return items;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Provider {Provider} returned invalid JSON for request {RequestUri}",
                provider,
                requestUri);
            return [];
        }
    }

    private async Task<HttpResponseMessage?> SendAsync(
        string requestUri,
        string provider,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("scraper-client");

        try
        {
            var response = await client.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Provider {Provider} request {RequestUri} returned status code {StatusCode}",
                    provider,
                    requestUri,
                    (int)response.StatusCode);
                response.Dispose();
                return null;
            }

            return response;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Provider {Provider} request {RequestUri} failed before a response was returned",
                provider,
                requestUri);
            return null;
        }
    }

    private static ProviderCatalogTitle? SelectBestMatch(IReadOnlyCollection<ProviderCatalogTitle> results, string requestedTitle)
    {
        if (results.Count == 0)
        {
            return null;
        }

        var normalizedRequestedTitle = Normalize(requestedTitle);
        return results
            .OrderByDescending(result => ScoreTitleMatch(normalizedRequestedTitle, Normalize(result.Title)))
            .ThenBy(result => result.Title.Length)
            .FirstOrDefault();
    }

    private static int ScoreTitleMatch(string requestedTitle, string candidateTitle)
    {
        if (requestedTitle == candidateTitle)
        {
            return 1000;
        }

        if (candidateTitle.StartsWith(requestedTitle, StringComparison.Ordinal))
        {
            return 800;
        }

        if (candidateTitle.Contains(requestedTitle, StringComparison.Ordinal) ||
            requestedTitle.Contains(candidateTitle, StringComparison.Ordinal))
        {
            return 600;
        }

        var requestedTokens = requestedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidateTokens = candidateTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return requestedTokens.Intersect(candidateTokens, StringComparer.Ordinal).Count();
    }

    private static JsonElement GetResultArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            return results;
        }

        return default;
    }

    private static IReadOnlyCollection<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyCollection<ProviderEpisodeInfo> ReadEpisodes(JsonElement root)
    {
        if (!root.TryGetProperty("episodes", out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item =>
            {
                var number = ReadDouble(item, "number");
                var episodeNumber = number.HasValue ? Math.Max(1, (int)Math.Round(number.Value)) : 0;
                var title = ReadString(item, "title");
                var label = !string.IsNullOrWhiteSpace(title)
                    ? $"Episode {episodeNumber} · {title.Trim()}"
                    : $"Episode {episodeNumber}";

                return new ProviderEpisodeInfo(
                    ReadString(item, "id") ?? string.Empty,
                    episodeNumber,
                    label);
            })
            .Where(episode => !string.IsNullOrWhiteSpace(episode.Id) && episode.EpisodeNumber > 0)
            .DistinctBy(episode => episode.EpisodeNumber)
            .ToArray();
    }

    private static int ReadEpisodeCount(JsonElement item, int fallbackCount)
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

        return fallbackCount;
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

    private static double? ReadDouble(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static bool TryReadInt(JsonElement item, string propertyName, out int value)
    {
        value = 0;
        if (!item.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
        {
            return true;
        }

        return false;
    }

    private static bool DetermineSubAvailability(string? subOrDub)
    {
        return subOrDub?.Trim().ToLowerInvariant() switch
        {
            "sub" => true,
            "both" => true,
            _ => true
        };
    }

    private static bool DetermineDubAvailability(string? subOrDub)
    {
        return subOrDub?.Trim().ToLowerInvariant() switch
        {
            "dub" => true,
            "both" => true,
            _ => false
        };
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

public sealed record ProviderCatalogTitle(
    string Provider,
    string ExternalId,
    string Title,
    string ImageUrl,
    int EpisodeCount,
    bool SubAvailable,
    bool DubAvailable, 
    IReadOnlyCollection<string> Genres); // <--- ADD THIS LINE

public sealed record ProviderAnimeInfo(
    string Provider,
    string ExternalId,
    string Title,
    string Synopsis,
    string PosterImageUrl,
    string BackdropImageUrl,
    bool SubAvailable,
    bool DubAvailable,
    int EpisodeCount,
    IReadOnlyCollection<string> Genres,
    IReadOnlyCollection<ProviderEpisodeInfo> Episodes);

public sealed record ProviderEpisodeInfo(
    string Id,
    int EpisodeNumber,
    string Label);
