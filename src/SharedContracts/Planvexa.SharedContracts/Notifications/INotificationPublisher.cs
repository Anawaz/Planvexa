namespace Planvexa.SharedContracts.Notifications;

/// <summary>Channels a notification can be delivered through.</summary>
[Flags]
public enum NotificationChannels
{
    None = 0,
    Inbox = 1,
    Email = 2,
    Push = 4,
}

/// <summary>
/// A request to create a notification for a recipient. Submitted by any module (e.g. Collaboration on
/// a mention) through <see cref="INotificationPublisher"/>. The Notifications module applies the
/// recipient's preferences, deduplicates on <see cref="DeduplicationKey"/>, and enqueues deliveries.
/// </summary>
public sealed record NotificationRequest(
    Guid RecipientUserId,
    string EventType,
    string EntityType,
    Guid EntityId,
    Guid WorkspaceId,
    string DeduplicationKey,
    IReadOnlyDictionary<string, string>? Payload = null);

/// <summary>
/// Cross-module contract (implemented by the Notifications module) so other modules can raise
/// notifications without touching notification tables directly (AGENTS.md rule 7). Runs under the
/// ambient tenant. Idempotent: a duplicate <see cref="NotificationRequest.DeduplicationKey"/> for the
/// same recipient is a no-op.
/// </summary>
public interface INotificationPublisher
{
    Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}
