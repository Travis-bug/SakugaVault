namespace SakugaVault.Models;

/// <summary>
/// Long-lived refresh token used to rotate short-lived access tokens.
/// These tokens are persisted so logout and token rotation can revoke specific credentials.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public bool IsRevoked { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } = null!;
}
