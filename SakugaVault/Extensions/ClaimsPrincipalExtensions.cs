using System.Security.Claims;

namespace SakugaVault.Extensions;

/// <summary>
/// Helpers for reading authenticated user information from JWT claims.
/// This keeps claim parsing out of controllers so endpoints stay focused on HTTP concerns.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var rawValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawValue, out var userId) ? userId : null;
    }
}
