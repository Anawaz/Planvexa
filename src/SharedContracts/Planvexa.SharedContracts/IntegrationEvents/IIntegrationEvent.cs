namespace Planvexa.SharedContracts.IntegrationEvents;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// Marker for events published across module boundaries via the outbox. Integration events are the
/// ONLY sanctioned way for one module to react to another module's state changes (AGENTS.md rule 7).
/// They are a specialization of <see cref="IDomainEvent"/> so aggregates can raise them directly.
/// </summary>
public interface IIntegrationEvent : IDomainEvent;

public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
}
