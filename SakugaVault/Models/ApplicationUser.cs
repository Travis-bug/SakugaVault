namespace SakugaVault.Models;

/// <summary>
/// Application user for SakugaVault.
/// This is intentionally a custom user model rather than ASP.NET Identity's full schema so the auth flow stays close to your HMS style while remaining easy to extend later.
/// </summary>
public sealed class ApplicationUser : EntityBase
{
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    public ICollection<AnimeComment> Comments { get; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; } = [];
    public ICollection<WatchHistoryEntry> WatchHistoryEntries { get; } = [];
}
