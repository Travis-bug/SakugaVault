namespace SakugaVault.Options;

/// <summary>
/// Strongly typed JWT configuration.
/// These values define how SakugaVault signs and validates the tokens used by the React client when calling the API.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const string SigningKeyEnvironmentVariable = "ASPNETCORE_JWT_SIGNINGKEY";

    public string Issuer { get; set; } = "SakugaVault";
    public string Audience { get; set; } = "SakugaVault.Client";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
}
