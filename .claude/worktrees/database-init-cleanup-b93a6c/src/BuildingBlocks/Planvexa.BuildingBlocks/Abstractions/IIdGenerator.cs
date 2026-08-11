namespace Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// Generates sortable, globally unique identifiers (UUIDv7).
/// See ADR-0014. Time-ordered IDs improve index locality in PostgreSQL.
/// </summary>
public interface IIdGenerator
{
    Guid NewId();
}

public sealed class UuidV7IdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7();
}
