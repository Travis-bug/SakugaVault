namespace SakugaVault.Options;

/// <summary>
/// Catalog-specific configuration such as cache duration for the expensive home catalog query.
/// </summary>
public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    public int HomeCatalogCacheMinutes { get; init; } = 5;
}
