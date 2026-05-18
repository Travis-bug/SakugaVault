using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Request payload for creating a comment on an anime title.
/// </summary>
public sealed record PostCommentRequestDto(
    [param: Required(ErrorMessage = "Anime selection is required.")] Guid AnimeId,
    [param: Required(ErrorMessage = "Comment text is required."), MaxLength(2000, ErrorMessage = "Comment text must be 2000 characters or fewer.")] string Body);
