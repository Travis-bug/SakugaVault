using SakugaVault.Contracts.Watch;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Contract for assembling watch-page data.
/// It isolates the controller from playback-resolution strategy, related-title logic, and future repository calls.
/// </summary>
public interface IWatchPageService
{
    Task<WatchPageDto?> GetWatchPageAsync(string animeId, CancellationToken cancellationToken);
}
