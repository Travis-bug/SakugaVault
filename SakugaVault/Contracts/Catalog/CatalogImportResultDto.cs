namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Aggregate result for a provider-backed catalog import.
/// This reports whether the run created new titles, updated existing ones, and optionally synced metadata.
/// </summary>
public sealed record CatalogImportResultDto(
    string Provider,
    string Feed,
    int PagesProcessed,
    int ImportedCount,
    int CreatedCount,
    int UpdatedCount,
    int MetadataSyncedCount,
    IReadOnlyCollection<ImportedCatalogTitleDto> Titles);
