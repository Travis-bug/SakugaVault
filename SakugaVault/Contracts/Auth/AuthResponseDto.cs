namespace SakugaVault.Contracts.Auth;

/// <summary>
/// Login, registration, and refresh response containing the current user snapshot plus a short-lived JWT access token.
/// The long-lived refresh token is transported separately in an HttpOnly cookie so it never enters client-side storage.
/// </summary>
public sealed record AuthResponseDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    CurrentUserDto User);
