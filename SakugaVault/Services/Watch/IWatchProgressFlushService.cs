namespace SakugaVault.Services.Watch;

public interface IWatchProgressFlushService
{
    Task<int> FlushAsync(Guid? userId, CancellationToken cancellationToken);
}
