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
    string StatusMessage);
