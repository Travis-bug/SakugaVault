using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SakugaVault.Contracts.Downloads;
using SakugaVault.Extensions;
using SakugaVault.Services.Downloads;

namespace SakugaVault.Controllers;

/// <summary>
/// Thin controller for the persisted download queue.
/// Requests are authenticated here, while queue validation and duplicate detection remain in the service layer.
/// </summary>
[ApiController]
[Route("api/downloads")]
[Authorize]
public sealed class DownloadsController(IDownloadQueueService downloadQueueService) : ControllerBase
{
    [HttpGet("me")]
    [EnableRateLimiting("catalog-read")]
    [ProducesResponseType(typeof(IReadOnlyCollection<DownloadQueueItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyCollection<DownloadQueueItemDto>>> GetMyQueue(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var queue = await downloadQueueService.GetUserQueueAsync(userId.Value, cancellationToken);
        return Ok(queue);
    }

    [HttpPost]
    [EnableRateLimiting("write-light")]
    [ProducesResponseType(typeof(DownloadQueueItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DownloadQueueItemDto>> QueueDownload(QueueDownloadRequestDto request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await downloadQueueService.QueueAsync(userId.Value, request, cancellationToken);
        if (!result.Succeeded)
        {
            var statusCode = result.ErrorCode switch
            {
                "anime_not_found" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest
            };

            return StatusCode(statusCode, new ProblemDetails
            {
                Title = "Download queue request failed",
                Detail = result.ErrorMessage,
                Status = statusCode
            });
        }

        return Created($"/api/downloads/{result.Value!.DownloadId}", result.Value);
    }

    [HttpDelete("{downloadId:guid}")]
    [EnableRateLimiting("write-light")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid downloadId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await downloadQueueService.RemoveAsync(userId.Value, downloadId, cancellationToken);
        if (!result.Succeeded)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Download queue removal failed",
                Detail = result.ErrorMessage,
                Status = StatusCodes.Status404NotFound
            });
        }

        return NoContent();
    }
}
