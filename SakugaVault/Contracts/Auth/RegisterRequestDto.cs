using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Auth;

/// <summary>
/// Registration request for a new SakugaVault account.
/// Validation attributes live here so invalid payloads fail before they ever reach the service layer.
/// </summary>
public sealed record RegisterRequestDto(
    [param: Required(ErrorMessage = "Display name is required."), StringLength(120, MinimumLength = 2, ErrorMessage = "Display name must be between 2 and 120 characters.")] string DisplayName,
    [param: Required(ErrorMessage = "Username is required."), StringLength(64, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 64 characters.")] string UserName,
    [param: Required(ErrorMessage = "Email is required."), EmailAddress(ErrorMessage = "Enter a valid email address."), StringLength(256, ErrorMessage = "Email must be 256 characters or fewer.")] string Email,
    [param: Required(ErrorMessage = "Password is required."), MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")] string Password);
