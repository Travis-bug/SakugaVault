namespace SakugaVault.Models;

/// <summary>
/// User-authored comment attached to an anime title.
/// Comments live in MySQL because they are relational application state and not media content.
/// </summary>
public sealed class AnimeComment : EntityBase
{
    public Guid AnimeId { get; set; }
    public Guid UserId { get; set; }
    public string Body { get; set; } = string.Empty;

    public Anime Anime { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
