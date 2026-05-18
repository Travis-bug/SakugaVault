namespace SakugaVault.Services.Scraping;

/// <summary>
/// Shared abstraction for provider-backed catalog, search, and metadata lookups.
/// This keeps Consumet-specific request shapes in one place so catalog and watch flows can swap providers cleanly.
/// </summary>
public interface IAnimeProviderClient
{
    Task<IReadOnlyCollection<ProviderCatalogTitle>> GetFeedAsync(
        string provider,
        string feed,
        int pageCount,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ProviderCatalogTitle>> SearchAsync(
        string provider,
        string query,
        int page,
        CancellationToken cancellationToken);

    Task<ProviderAnimeInfo?> GetAnimeInfoAsync(
        string provider,
        string externalId,
        CancellationToken cancellationToken);

    Task<ProviderAnimeInfo?> FindAnimeInfoByTitleAsync(
        string provider,
        string title,
        CancellationToken cancellationToken);
}
