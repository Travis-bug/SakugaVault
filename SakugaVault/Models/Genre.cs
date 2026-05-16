namespace SakugaVault.Models;

/// <summary>
/// Genre taxonomy used to build the catalog rails and similar-title matching.
/// This stays normalized so one title can belong to many rails without duplicated text.
/// </summary>
public sealed class Genre : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    public ICollection<AnimeGenre> AnimeGenres { get; } = [];
}
