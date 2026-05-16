using SakugaVault.Contracts.Watch;
using SakugaVault.Services.Common;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Service boundary for resolving a playable source for the watch page.
/// This keeps scraper orchestration and playback policy out of the controller.
/// </summary>
public interface IPlaybackResolutionService
{
    Task<OperationResult<ResolvedPlaybackDto>> ResolveAsync(Guid animeId, PlaybackResolutionRequestDto request, CancellationToken cancellationToken);
}
