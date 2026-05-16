using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Request model for cursor-based watch history pagination.
/// </summary>
public sealed record CursorPageRequestDto(
    string? Cursor,
    [property: Range(1, 100)] int PageSize = 20);
