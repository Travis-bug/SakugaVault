namespace SakugaVault.Contracts.Common;

/// <summary>
/// Cursor-based pagination wrapper for feeds that should not expose offset pagination semantics.
/// </summary>
public sealed record CursorPagedResult<T>(
    IReadOnlyCollection<T> Items,
    string? NextCursor,
    int PageSize,
    bool HasMore);
