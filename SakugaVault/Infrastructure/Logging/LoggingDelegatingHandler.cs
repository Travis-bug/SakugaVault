using System.Diagnostics;
using SakugaVault.Extensions;

namespace SakugaVault.Infrastructure.Logging;

/// <summary>
/// Logs outbound HTTP requests made through the scraper client and carries the current correlation ID downstream.
/// </summary>
public sealed class LoggingDelegatingHandler(
    IHttpContextAccessor httpContextAccessor,
    ILogger<LoggingDelegatingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = httpContextAccessor.HttpContext?.Items[HttpContextItemKeys.CorrelationId]?.ToString();
        if (!string.IsNullOrWhiteSpace(correlationId) && !request.Headers.Contains("X-Correlation-Id"))
        {
            request.Headers.Add("X-Correlation-Id", correlationId);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            logger.LogInformation(
                "Outgoing HTTP request completed: {Method} {Url} -> {StatusCode} in {DurationMs}ms",
                request.Method.Method,
                request.RequestUri,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();

            logger.LogError(
                exception,
                "Outgoing HTTP request failed: {Method} {Url} after {DurationMs}ms",
                request.Method.Method,
                request.RequestUri,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}
