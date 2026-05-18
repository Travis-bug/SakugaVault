namespace SakugaVault.Options;

/// <summary>
/// Strongly typed settings for short-lived access tokens.
/// The signing key is sourced from ASPNETCORE_JWT_SIGNINGKEY at runtime and never committed into appsettings files.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "SakugaVault";
    public string Audience { get; set; } = "SakugaVault.Web";
    public int AccessTokenMinutes { get; set; } = 60;
    public string SigningKey { get; set; } = string.Empty;
}
