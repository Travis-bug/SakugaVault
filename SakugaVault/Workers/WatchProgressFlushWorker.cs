using SakugaVault.Services.Watch;

namespace SakugaVault.Workers;

/// <summary>
/// Periodically drains Redis-buffered watch progress into MySQL so casual navigation still becomes durable.
/// </summary>
public sealed class WatchProgressFlushWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WatchProgressFlushWorker> logger) : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await FlushAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await FlushAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var flushService = scope.ServiceProvider.GetRequiredService<IWatchProgressFlushService>();
            var flushed = await flushService.FlushAsync(null, cancellationToken);
            if (flushed > 0)
            {
                logger.LogInformation("Flushed {Count} Redis watch-progress entries to MySQL.", flushed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Periodic watch-progress flush failed.");
        }
    }
}
