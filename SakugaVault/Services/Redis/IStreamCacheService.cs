using SakugaVault.Services.Scraping;

namespace SakugaVault.Services.Redis;

public interface IStreamCacheService
{
    Task<StreamScrapeResult?> GetAsync(StreamCacheKey key, CancellationToken cancellationToken);
    Task SetAsync(StreamCacheKey key, StreamScrapeResult result, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid animeId, CancellationToken cancellationToken);
}
