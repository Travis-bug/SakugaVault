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
    public ScraperResolverOptions[] PlaybackResolvers { get; init; } = [];
    public int RequestTimeoutSeconds { get; init; } = 15;
    public bool EnableHostScrapers { get; init; } = true;
    public string[] FallbackProviders { get; init; } = [];
    public int InterRequestDelayMilliseconds { get; init; } = 500;
}

/// <summary>
/// One independently hosted playback resolver. Each resolver must expose the
/// small Consumet-compatible info and watch surface used by SakugaVault.
/// </summary>
public sealed class ScraperResolverOptions
{
    public string Name { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public int Priority { get; init; } = 100;
    public int RequestTimeoutSeconds { get; init; }
    public Dictionary<string, string> RequestHeaders { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
