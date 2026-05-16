namespace SakugaVault.Models;

/// <summary>
/// Explicit join entity between anime and genres.
/// This is kept separate because it gives you room for future ranking or source metadata per association.
/// </summary>
public sealed class AnimeGenre
{
    public Guid AnimeId { get; set; }
    public Guid GenreId { get; set; }

    public Anime Anime { get; set; } = null!;
    public Genre Genre { get; set; } = null!;
}
