namespace Planvexa.BuildingBlocks.Domain;

/// <summary>
/// Base class for all entities. Identity is a UUIDv7 <see cref="Guid"/>.
/// Aggregates collect domain events which the persistence layer converts into outbox messages.
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = new();

    protected Entity(Guid id) => Id = id;

    // Parameterless ctor for EF Core materialization.
    protected Entity()
    {
    }

    public Guid Id { get; protected set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();

    public override bool Equals(object? obj)
        => obj is Entity other && other.GetType() == GetType() && other.Id == Id && Id != Guid.Empty;

    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>Marks the consistency boundary / transactional root of a cluster of entities.</summary>
public interface IAggregateRoot;
