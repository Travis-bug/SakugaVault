namespace SakugaVault.Options;

/// <summary>
/// Strongly typed settings for external resolver and scraper behavior.
/// The API keeps these values in configuration because host endpoints and timeouts are environment concerns,
/// not business rules.
/// </summary>
public sealed class ScraperOptions
{
    public const string SectionName = "Scrapers";

    public string ConsumetBaseUrl { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; } = 15;
    public bool EnableHostScrapers { get; init; } = true;
    public string[] FallbackProviders { get; init; } = [];
    public int InterRequestDelayMilliseconds { get; init; } = 500;
}
