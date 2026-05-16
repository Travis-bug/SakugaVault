namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Represents one horizontal catalog row such as Action or Romance.
/// The service aggregates titles into these rails because grouping content is business logic,
/// not controller logic.
/// </summary>
public sealed record GenreRailDto(
    string Genre,
    IReadOnlyCollection<AnimeCardDto> Titles);
