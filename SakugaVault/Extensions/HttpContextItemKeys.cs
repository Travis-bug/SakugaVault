namespace SakugaVault.Extensions;

/// <summary>
/// Centralized keys for HttpContext.Items values set by middleware.
/// </summary>
public static class HttpContextItemKeys
{
    public const string CorrelationId = "CorrelationId";
}
