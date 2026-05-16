using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Auth;

/// <summary>
/// Login request using either username or email plus password.
/// Keeping this separate from the entity avoids leaking persistence concerns into the API contract.
/// </summary>
public sealed record LoginRequestDto(
    [property: Required, StringLength(256)] string Identifier,
    [property: Required, MinLength(8)] string Password);
