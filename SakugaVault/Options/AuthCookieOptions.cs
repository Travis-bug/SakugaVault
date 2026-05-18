namespace SakugaVault.Options;

/// <summary>
/// Strongly typed settings for the refresh-token cookie.
/// The cookie carries only the opaque refresh token; short-lived JWT access tokens stay in the API response body.
/// </summary>
public sealed class AuthCookieOptions
{
    public const string SectionName = "Authentication";

    public string CookieName { get; init; } = "SakugaVault.Refresh";
    public int RefreshTokenDays { get; init; } = 7;
}
