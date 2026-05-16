using SakugaVault.Extensions;

namespace SakugaVault.Middleware;

/// <summary>
/// Assigns a correlation ID to every inbound request and echoes it back in the response.
/// This makes it possible to trace user-triggered flows across controllers, services, and outbound HTTP calls.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue("X-Correlation-Id", out var existingValue) &&
                            !string.IsNullOrWhiteSpace(existingValue)
            ? existingValue.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[HttpContextItemKeys.CorrelationId] = correlationId;
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object?>
               {
                   ["CorrelationId"] = correlationId
               }))
        {
            await next(context);
        }
    }
}
