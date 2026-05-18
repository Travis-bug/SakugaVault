namespace SakugaVault.Options;

/// <summary>
/// Catalog-specific configuration such as cache duration for the expensive home catalog query.
/// </summary>
public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    public int HomeCatalogCacheMinutes { get; init; } = 5;
    public bool EnableDevelopmentSeedData { get; init; } = true;
    public bool UseLiveProviderCatalog { get; init; }
    public string HomeFeed { get; init; } = "top-airing";
    public int HomePageCount { get; init; } = 2;
    public int LiveCatalogTitleLimit { get; init; } = 24;
    public string[] PreferredProviders { get; init; } = [];
}
