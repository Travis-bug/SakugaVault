namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Lightweight card data for a catalog tile in the React client.
/// This stays intentionally small because controllers should return DTOs shaped for the UI,
/// not database entities or oversized payloads.
/// </summary>
public sealed record AnimeCardDto(
    string Id,
    string Title,
    string CoverImageUrl,
    int EpisodeCount,
    bool SubAvailable,
    bool DubAvailable,
    string WatchRoute);
