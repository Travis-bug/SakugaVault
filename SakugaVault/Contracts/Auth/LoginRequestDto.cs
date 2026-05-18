using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Auth;

/// <summary>
/// Login request using either username or email plus password.
/// Keeping this separate from the entity avoids leaking persistence concerns into the API contract.
/// </summary>
public sealed record LoginRequestDto(
    [param: Required(ErrorMessage = "Username or email is required."), StringLength(256, ErrorMessage = "Username or email must be 256 characters or fewer.")] string Identifier,
    [param: Required(ErrorMessage = "Password is required."), MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")] string Password);
