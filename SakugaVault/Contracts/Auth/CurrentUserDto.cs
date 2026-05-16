namespace SakugaVault.Contracts.Auth;

/// <summary>
/// Authenticated user profile returned by the API.
/// The frontend uses this to hydrate session state without exposing password or normalization fields.
/// </summary>
public sealed record CurrentUserDto(
    Guid Id,
    string DisplayName,
    string UserName,
    string Email);
