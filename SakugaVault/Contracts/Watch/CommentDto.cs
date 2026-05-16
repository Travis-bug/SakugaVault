namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Read model for a single watch-page comment.
/// This exists as a DTO boundary so the API can evolve separately from the future persistence model.
/// </summary>
public sealed record CommentDto(
    string UserName,
    string Body,
    DateTimeOffset PostedAtUtc);
