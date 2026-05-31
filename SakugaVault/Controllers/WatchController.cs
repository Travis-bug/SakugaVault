using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SakugaVault.Contracts.Common;
using SakugaVault.Contracts.Watch;
using SakugaVault.Extensions;
using SakugaVault.Services.Metadata;
using SakugaVault.Services.Watch;

namespace SakugaVault.Controllers;

/// <summary>
/// Thin API controller for the watch experience.
/// This exists to keep request routing and HTTP concerns separate from the logic that shapes
/// metadata, playback hints, comments, and similar-title recommendations.
/// </summary>
[ApiController]
[Route("api/watch")]
[Authorize]
public sealed class WatchController(
    IWatchPageService watchPageService,
    IWatchHistoryService watchHistoryService,
    IPlaybackResolutionService playbackResolutionService,
    IPlaybackStreamProxyService playbackStreamProxyService,
    IMetadataSyncService metadataSyncService) : ControllerBase
{
    /// <summary>
    /// Returns the data needed to render one watch page.
    /// Not-found handling stays here because mapping business outcomes to HTTP status codes is a controller concern.
    /// </summary>
    [HttpGet("{animeId}")]
    [ProducesResponseType(typeof(WatchPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WatchPageDto>> GetWatchPage(string animeId, CancellationToken cancellationToken)
    {
        var watchPage = await watchPageService.GetWatchPageAsync(animeId, cancellationToken);
        if (watchPage is null)
        {
            return NotFound();
        }

        return Ok(watchPage);
    }

    /// <summary>
    /// Returns the current user's playback history.
    /// This powers continue-watching and profile history views.
    /// </summary>
    [HttpGet("history/me")]
    [ProducesResponseType(typeof(CursorPagedResult<WatchHistoryEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CursorPagedResult<WatchHistoryEntryDto>>> GetCurrentUserHistory(
        [FromQuery] CursorPageRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var history = await watchHistoryService.GetUserHistoryAsync(userId.Value, request.Cursor, request.PageSize, cancellationToken);
        return Ok(history);
    }

    /// <summary>
    /// Saves or updates playback progress for the current user.
    /// The controller only reads the auth context and forwards the request to the service.
    /// </summary>
    [HttpPost("history")]
    [ProducesResponseType(typeof(WatchHistoryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WatchHistoryEntryDto>> UpsertWatchHistory(UpsertWatchHistoryRequestDto request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await watchHistoryService.UpsertAsync(userId.Value, request, cancellationToken);
        if (!result.Succeeded)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Watch history update failed",
                Detail = result.ErrorMessage,
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Resolves a playable source for an episode.
    /// The resolver pipeline is wired now so host adapters can be added without changing controller structure.
    /// </summary>
    [HttpPost("{animeId:guid}/resolve-playback")]
    [ProducesResponseType(typeof(ResolvedPlaybackDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResolvedPlaybackDto>> ResolvePlayback(Guid animeId, PlaybackResolutionRequestDto request, CancellationToken cancellationToken)
    {
        var result = await playbackResolutionService.ResolveAsync(animeId, request, cancellationToken);
        if (!result.Succeeded)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Playback resolution failed",
                Detail = result.ErrorMessage,
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Proxies a short-lived provider stream URL through the API.
    /// The stream id is generated only after authenticated playback resolution succeeds.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("streams/{streamId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProxyResolvedStream(Guid streamId, CancellationToken cancellationToken)
    {
        var proxied = await playbackStreamProxyService.ProxyAsync(streamId, Request, Response, cancellationToken);
        return proxied ? new EmptyResult() : NotFound();
    }

    /// <summary>
    /// Triggers a metadata refresh for one title.
    /// This is where provider-specific synchronization workflows attach later.
    /// </summary>
    [HttpPost("{animeId:guid}/sync-metadata")]
    [ProducesResponseType(typeof(MetadataSyncResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MetadataSyncResultDto>> SyncMetadata(Guid animeId, CancellationToken cancellationToken)
    {
        var result = await metadataSyncService.SyncAnimeMetadataAsync(animeId, cancellationToken);
        if (!result.Succeeded)
        {
            var statusCode = result.ErrorCode switch
            {
                "external_metadata_missing" => StatusCodes.Status400BadRequest,
                "metadata_sync_failed" => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status404NotFound
            };

            return StatusCode(statusCode, new ProblemDetails
            {
                Title = "Metadata sync failed",
                Detail = result.ErrorMessage,
                Status = statusCode
            });
        }

        return Ok(result.Value);
    }
}
