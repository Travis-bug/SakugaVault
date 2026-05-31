using SakugaVault.Services.Scraping;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Creates short-lived local stream URLs for provider media that browsers cannot load reliably from the upstream host.
/// </summary>
public interface IPlaybackStreamProxyService
{
    string Register(StreamScrapeResult stream);
    string RegisterUrl(string url, IReadOnlyDictionary<string, string>? headers = null);
    Task<bool> ProxyAsync(Guid streamId, HttpRequest request, HttpResponse response, CancellationToken cancellationToken);
}
