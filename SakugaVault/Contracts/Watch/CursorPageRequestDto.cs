using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Request model for cursor-based watch history pagination.
/// </summary>
public sealed record CursorPageRequestDto(
    string? Cursor,
    [param: Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")] int PageSize = 20);
