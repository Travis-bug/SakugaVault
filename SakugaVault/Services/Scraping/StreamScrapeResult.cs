namespace SakugaVault.Services.Scraping;

/// <summary>
/// Internal result returned by scraper services before it is translated into API DTOs.
/// This keeps scraper implementation details out of the public contracts.
/// </summary>
public sealed record StreamScrapeResult(
    bool IsResolved,
    string PreferredProtocol,
    string? StreamUrl,
    string? SourceHost,
    string Provider,
    string StatusMessage)
{
    public string Resolver { get; init; } = string.Empty;
    public int ResolverPriority { get; init; } = 100;
    public string? AudioLanguage { get; init; }
    public string? SubtitleLanguage { get; init; }
    public string? LanguageWarning { get; init; }
    public IReadOnlyDictionary<string, string> SourceRequestHeaders { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<StreamSubtitleTrack> SubtitleTracks { get; init; } = [];
}

public sealed record StreamSubtitleTrack(
    string Url,
    string Language,
    string Label,
    string Kind);
