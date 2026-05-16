using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Auth;

/// <summary>
/// Request body for refresh-token rotation and logout flows.
/// </summary>
public sealed record RefreshTokenRequestDto(
    [property: Required, MinLength(32)] string RefreshToken);
