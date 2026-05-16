namespace SakugaVault.Options;

/// <summary>
/// Strongly typed settings for the decoupled React frontend.
/// Binding these values keeps CORS configuration out of magic strings and makes environment overrides cleaner.
/// </summary>
public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    public string[] AllowedOrigins { get; init; } = [];
}
