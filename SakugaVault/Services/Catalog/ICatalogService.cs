using SakugaVault.Services.Common;
using SakugaVault.Contracts.Catalog;

namespace SakugaVault.Services.Catalog;

/// <summary>
/// Contract for catalog business logic.
/// Controllers depend on this abstraction so the API layer stays testable and does not couple itself
/// to one concrete data source or implementation.
/// </summary>
public interface ICatalogService
{
    Task<HomeCatalogDto> GetHomeCatalogAsync(CancellationToken cancellationToken);
    Task<CatalogSearchResponseDto> SearchAsync(string? query, int limit, CancellationToken cancellationToken);
    Task<OperationResult<CommentPostedDto>> PostCommentAsync(Guid userId, PostCommentRequestDto request, CancellationToken cancellationToken);
}
