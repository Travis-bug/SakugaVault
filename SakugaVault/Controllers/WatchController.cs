using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    IWatchProgressFlushService watchProgressFlushService,
    IPlaybackResolutionService playbackResolutionService,
    IPlaybackStreamProxyService playbackStreamProxyService,
    IMetadataSyncService metadataSyncService,
    IEpisodeListService episodeListService) : ControllerBase
{
    /// <summary>
    /// Returns the data needed to render one watch page.
    /// Not-found handling stays here because mapping business outcomes to HTTP status codes is a controller concern.
    /// </summary>
    [HttpGet("{animeId}")]
    [EnableRateLimiting("catalog-read")]
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
    /// Returns provider-backed episodes after the page has rendered from local metadata.
    /// </summary>
    [HttpGet("{animeId:guid}/episodes")]
    [EnableRateLimiting("catalog-read")]
    [ProducesResponseType(typeof(EpisodeListResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EpisodeListResponseDto>> GetEpisodeList(Guid animeId, CancellationToken cancellationToken)
    {
        var result = await episodeListService.GetEpisodesAsync(animeId, cancellationToken);
        if (!result.IsResolved && string.Equals(result.StatusMessage, "Anime not found.", StringComparison.Ordinal))
        {
            return NotFound();
        }

        return Ok(new EpisodeListResponseDto(
            result.IsResolved,
            result.ProviderKey,
            result.Seasons,
            result.StatusMessage));
    }

    /// <summary>
    /// Returns the current user's playback history.
    /// This powers continue-watching and profile history views.
    /// </summary>
    [HttpGet("history/me")]
    [EnableRateLimiting("catalog-read")]
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
    [EnableRateLimiting("write-light")]
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

    [HttpPost("history/flush")]
    [EnableRateLimiting("write-light")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> FlushWatchHistory(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        await watchProgressFlushService.FlushAsync(userId.Value, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Resolves a playable source for an episode.
    /// The resolver pipeline is wired now so host adapters can be added without changing controller structure.
    /// </summary>
    [HttpPost("{animeId:guid}/resolve-playback")]
    [EnableRateLimiting("playback-resolve")]
    [ProducesResponseType(typeof(ResolvedPlaybackDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResolvedPlaybackDto>> ResolvePlayback(Guid animeId, PlaybackResolutionRequestDto request, CancellationToken cancellationToken)
    {
        var result = await playbackResolutionService.ResolveAsync(animeId, request, cancellationToken);
        if (!result.Succeeded)
        {
            var statusCode = result.ErrorCode switch
            {
                "scrape_rate_limited" => StatusCodes.Status429TooManyRequests,
                "anime_not_found" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status404NotFound
            };

            if (result.RetryAfter is { TotalSeconds: > 0 } retryAfter)
            {
                Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString("0");
            }

            return StatusCode(statusCode, new ProblemDetails
            {
                Title = "Playback resolution failed",
                Detail = result.ErrorMessage,
                Status = statusCode
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
    [EnableRateLimiting("stream-proxy")]
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
    [EnableRateLimiting("metadata-sync")]
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
