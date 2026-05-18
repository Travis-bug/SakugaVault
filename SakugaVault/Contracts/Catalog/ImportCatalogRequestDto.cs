using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Developer-facing request payload for importing titles from an upstream anime provider feed.
/// This is intentionally not surfaced in the React UI because catalog import is an operator workflow.
/// </summary>
public sealed record ImportCatalogRequestDto(
    [param: Required(ErrorMessage = "A metadata provider is required.")]
    [param: MaxLength(64, ErrorMessage = "Provider names cannot exceed 64 characters.")]
    string Provider,
    [param: Required(ErrorMessage = "A feed name is required.")]
    [param: MaxLength(64, ErrorMessage = "Feed names cannot exceed 64 characters.")]
    string Feed,
    [param: Range(1, 5, ErrorMessage = "Import page count must be between 1 and 5.")]
    int PageCount = 1,
    bool SyncMetadata = false);
