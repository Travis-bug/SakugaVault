namespace SakugaVault.Models;

/// <summary>
/// Base type for entities that are persisted in MySQL.
/// Keeping shared identity and audit fields here reduces repetition and makes the database model easier to reason about.
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
