using SakugaVault.Contracts.Catalog;
using SakugaVault.Services.Common;

namespace SakugaVault.Services.Catalog;

/// <summary>
/// Imports catalog rows from an upstream provider feed into the local database.
/// This exists so development and operations can populate the catalog without relying on hard-coded seed rows.
/// </summary>
public interface ICatalogImportService
{
    Task<OperationResult<CatalogImportResultDto>> ImportFromProviderAsync(ImportCatalogRequestDto request, CancellationToken cancellationToken);
}
