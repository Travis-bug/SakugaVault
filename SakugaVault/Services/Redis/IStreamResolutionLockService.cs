namespace SakugaVault.Services.Redis;

public interface IStreamResolutionLockService
{
    Task<IAsyncDisposable?> TryAcquireAsync(StreamCacheKey key, TimeSpan lifetime, CancellationToken cancellationToken);
}
