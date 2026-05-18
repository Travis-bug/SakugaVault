namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Search response for the React discovery screen.
/// The frontend can render both the current query and the result set without needing to infer
/// whether the backend returned live search matches or a trending fallback collection.
/// </summary>
public sealed record CatalogSearchResponseDto(
    string Query,
    int TotalResults,
    IReadOnlyCollection<SearchAnimeResultDto> Results);
