using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Catalog;

/// <summary>
/// Request payload for creating a comment on an anime title.
/// </summary>
public sealed record PostCommentRequestDto(
    [property: Required] Guid AnimeId,
    [property: Required, MaxLength(2000)] string Body);
