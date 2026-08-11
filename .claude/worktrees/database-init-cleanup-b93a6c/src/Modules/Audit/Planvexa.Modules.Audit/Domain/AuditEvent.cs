namespace Planvexa.Modules.Audit.Domain;

/// <summary>
/// Append-only audit record (ADR-0012). Written in the same transaction as the change it describes.
/// Never updated or deleted.
/// </summary>
public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    public AuditEvent(
        Guid id,
        Guid? workspaceId,
        Guid? actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        string? data,
        string? correlationId,
        string? ipAddress,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        WorkspaceId = workspaceId;
        ActorUserId = actorUserId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Data = data;
        CorrelationId = correlationId;
        IpAddress = ipAddress;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>Null for platform-level events; otherwise the owning workspace.</summary>
    public Guid? WorkspaceId { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public Guid? EntityId { get; private set; }

    /// <summary>JSON payload with additional context (already redacted of secrets).</summary>
    public string? Data { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? IpAddress { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
