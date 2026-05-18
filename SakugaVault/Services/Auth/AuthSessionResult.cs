using SakugaVault.Contracts.Auth;

namespace SakugaVault.Services.Auth;

/// <summary>
/// Internal auth result used between the controller and service layer.
/// The access token is returned to the SPA, while the refresh token is written into an HttpOnly cookie by the controller.
/// </summary>
public sealed record AuthSessionResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    CurrentUserDto User);
