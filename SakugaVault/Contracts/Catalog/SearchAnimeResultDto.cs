namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Richer title card for the search experience.
/// Search results expose a synopsis snippet and genre labels because users are deciding whether to
/// open a title, not just browsing a compact rail.
/// </summary>
public sealed record SearchAnimeResultDto(
    string Id,
    string Title,
    string Synopsis,
    string PosterImageUrl,
    int EpisodeCount,
    bool SubAvailable,
    bool DubAvailable,
    string WatchRoute,
    IReadOnlyCollection<string> Genres);
