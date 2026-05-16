using SakugaVault.Models;

namespace SakugaVault.Services.Scraping;

/// <summary>
/// Boundary for host-specific playback scraping.
/// The rest of the application should ask for a resolved stream through this interface rather than hard-coding provider logic into controllers or watch services.
/// </summary>
public interface IStreamScraperService
{
    Task<StreamScrapeResult> ResolveStreamAsync(
        Anime anime,
        int episodeNumber,
        string preferredLanguage,
        string? providerOverride,
        CancellationToken cancellationToken);
}
