namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Summary of one title touched by a provider import run.
/// Swagger returns this so a developer can audit what was created or updated.
/// </summary>
public sealed record ImportedCatalogTitleDto(
    Guid AnimeId,
    string Title,
    string ExternalMetadataId,
    string PosterImageUrl,
    bool Created,
    bool MetadataSynced);
