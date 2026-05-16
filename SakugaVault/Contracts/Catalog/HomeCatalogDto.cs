namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Top-level response for the React catalog screen.
/// It combines the featured hero title with the genre rails so the client can render the page
/// with a single API call.
/// </summary>
public sealed record HomeCatalogDto(
    CatalogHeroDto HeroBanner,
    IReadOnlyCollection<GenreRailDto> GenreRows);
