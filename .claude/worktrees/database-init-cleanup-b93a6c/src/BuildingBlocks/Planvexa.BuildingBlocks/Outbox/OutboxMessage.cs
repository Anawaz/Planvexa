namespace Planvexa.BuildingBlocks.Outbox;

/// <summary>
/// Transactional outbox record. Domain events and integration events are written to this table in
/// the same transaction as the state change (ADR-0005). A background dispatcher publishes them
/// exactly-once (dedup by <see cref="Id"/>) and then marks them processed.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Nullable: platform-level events have no owning workspace.</summary>
    public Guid? WorkspaceId { get; set; }

    public required string Type { get; set; }

    public required string Payload { get; set; }

    public DateTimeOffset OccurredOnUtc { get; set; }

    public DateTimeOffset? ProcessedOnUtc { get; set; }

    public int Attempts { get; set; }

    public string? Error { get; set; }

    public string? CorrelationId { get; set; }
}
