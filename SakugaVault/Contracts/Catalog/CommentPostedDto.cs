namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Response returned after a comment is successfully created.
/// </summary>
public sealed record CommentPostedDto(
    Guid CommentId,
    Guid AnimeId,
    string AuthorDisplayName,
    string Body,
    DateTimeOffset CreatedAtUtc);
