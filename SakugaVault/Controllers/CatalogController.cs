using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SakugaVault.Contracts.Catalog;
using SakugaVault.Extensions;
using SakugaVault.Services.Catalog;

namespace SakugaVault.Controllers;

/// <summary>
/// Thin API controller for the catalog experience.
/// During the MVC-to-API refactor this replaced the old Razor HomeController so the backend now
/// behaves as a data service for React instead of rendering HTML on the server.
/// </summary>
[ApiController]
[Route("api/catalog")]
[Authorize]
public sealed class CatalogController(
    ICatalogService catalogService,
    ICatalogImportService catalogImportService,
    IBatchMetadataSyncService batchMetadataSyncService) : ControllerBase
{
    /// <summary>
    /// Returns the data required to render the React catalog home page.
    /// The controller does no shaping beyond delegating to the service and wrapping the result in 200 OK.
    /// </summary>
    [HttpGet("home")]
    [EnableRateLimiting("catalog-read")]
    [ProducesResponseType(typeof(HomeCatalogDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<HomeCatalogDto>> GetHomeCatalog(CancellationToken cancellationToken)
    {
        var catalog = await catalogService.GetHomeCatalogAsync(cancellationToken);
        return Ok(catalog);
    }

    [HttpGet("search")]
    [EnableRateLimiting("catalog-read")]
    [ProducesResponseType(typeof(CatalogSearchResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CatalogSearchResponseDto>> Search(
        [FromQuery] string? q,
        [FromQuery] int limit = 18,
        CancellationToken cancellationToken = default)
    {
        var results = await catalogService.SearchAsync(q, limit, cancellationToken);
        return Ok(results);
    }

    [HttpPost("comments")]
    [EnableRateLimiting("write-light")]
    [ProducesResponseType(typeof(CommentPostedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommentPostedDto>> PostComment(PostCommentRequestDto request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await catalogService.PostCommentAsync(userId.Value, request, cancellationToken);
        if (!result.Succeeded)
        {
            var statusCode = result.ErrorCode switch
            {
                "anime_not_found" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest
            };

            return StatusCode(statusCode, new ProblemDetails
            {
                Title = "Comment creation failed",
                Detail = result.ErrorMessage,
                Status = statusCode
            });
        }

        return Created($"/api/catalog/comments/{result.Value!.CommentId}", result.Value);
    }

    /// <summary>
    /// Imports titles from a provider feed into the local catalog.
    /// This is a developer/operator workflow and is intentionally not exposed in the public React UI.
    /// </summary>
    [HttpPost("import-provider")]
    [EnableRateLimiting("metadata-sync")]
    [ProducesResponseType(typeof(CatalogImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<CatalogImportResultDto>> ImportProviderCatalog(ImportCatalogRequestDto request, CancellationToken cancellationToken)
    {
        var result = await catalogImportService.ImportFromProviderAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            var statusCode = result.ErrorCode switch
            {
                "catalog_import_failed" => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status400BadRequest
            };

            return StatusCode(statusCode, new ProblemDetails
            {
                Title = "Catalog import failed",
                Detail = result.ErrorMessage,
                Status = statusCode
            });
        }

        return Ok(result.Value);
    }

    [HttpPost("sync-metadata")]
    [EnableRateLimiting("metadata-sync")]
    [ProducesResponseType(typeof(BatchSyncResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BatchSyncResultDto>> BatchSyncMetadata(BatchSyncRequestDto request, CancellationToken cancellationToken)
    {
        var result = await batchMetadataSyncService.BatchSyncAsync(request.AnimeIds, cancellationToken);
        return Ok(result);
    }
}
