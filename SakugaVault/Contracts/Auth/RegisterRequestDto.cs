using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Auth;

/// <summary>
/// Registration request for a new SakugaVault account.
/// Validation attributes live here so invalid payloads fail before they ever reach the service layer.
/// </summary>
public sealed record RegisterRequestDto(
    [property: Required, StringLength(120, MinimumLength = 2)] string DisplayName,
    [property: Required, StringLength(64, MinimumLength = 3)] string UserName,
    [property: Required, EmailAddress, StringLength(256)] string Email,
    [property: Required, MinLength(8)] string Password);
