namespace SakugaVault.Contracts.Auth;

/// <summary>
/// Login and registration response containing the signed JWT plus the current user snapshot.
/// This is the contract the React client will store after a successful auth flow.
/// </summary>
public sealed record AuthResponseDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    CurrentUserDto User);
