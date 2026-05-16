namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Payload for the large catalog hero banner on the home page.
/// The refactor introduced this dedicated DTO so the service can prepare React-ready data
/// without forcing the controller to know how the screen is composed.
/// </summary>
public sealed record CatalogHeroDto(
    string Id,
    string Title,
    string Synopsis,
    string PosterImageUrl,
    string BackdropImageUrl,
    string WatchRoute);
