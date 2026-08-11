namespace Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// A realtime change signal broadcast to connected clients. The database is authoritative — this
/// only tells clients that something changed so they can refetch. Every event carries workspace,
/// entity and correlation identifiers so clients can reject anything outside their scope.
/// </summary>
public sealed record RealtimeEvent(
    Guid WorkspaceId,
    string EntityType,
    Guid EntityId,
    string Action,
    long? Version,
    string CorrelationId);

/// <summary>
/// Cross-cutting realtime broadcast capability. Modules depend on this abstraction; the concrete
/// implementation (SignalR) lives in the API host. A no-op default keeps non-realtime hosts and unit
/// tests working without wiring SignalR.
/// </summary>
public interface IRealtimeNotifier
{
    Task NotifyAsync(RealtimeEvent realtimeEvent, CancellationToken cancellationToken = default);
}

public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task NotifyAsync(RealtimeEvent realtimeEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
