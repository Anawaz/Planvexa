namespace Planvexa.Modules.Notifications.Application;

using System.Text.Json;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Notifications.Domain;
using Planvexa.SharedContracts.Notifications;

/// <summary>
/// Implements the cross-module <see cref="INotificationPublisher"/>. Applies the recipient's channel
/// preferences, deduplicates on (recipient, key), persists the durable notification and enqueues the
/// per-channel deliveries in one transaction. Idempotent: a duplicate key is a safe no-op.
/// </summary>
public sealed class NotificationPublisher(
    IWorkspaceContextAccessor workspaceAccessor,
    INotificationStore notifications,
    INotificationPreferenceStore preferences,
    IIdGenerator ids,
    IClock clock,
    IUnitOfWork unitOfWork) : INotificationPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = workspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return;
        }

        var workspaceId = request.WorkspaceId;

        // Dedup: same recipient + key => no-op (safe against duplicate delivery).
        if (await notifications.ExistsAsync(workspaceId, request.RecipientUserId, request.DeduplicationKey, cancellationToken))
        {
            return;
        }

        var preference = await preferences.FindAsync(workspaceId, request.RecipientUserId, request.EventType, cancellationToken);
        var channels = NotificationPolicy.Resolve(request.EventType, preference);
        if (channels == NotificationChannels.None)
        {
            return; // The user opted out of all channels for this event.
        }

        var now = clock.UtcNow;
        var payload = request.Payload is null ? null : JsonSerializer.Serialize(request.Payload, JsonOptions);

        var notification = Notification.Create(
            ids.NewId(), request.WorkspaceId, request.RecipientUserId, request.EventType,
            request.EntityType, request.EntityId, payload, request.DeduplicationKey, now);

        if (channels.HasFlag(NotificationChannels.Inbox))
        {
            notification.AddDelivery(ids.NewId(), NotificationChannels.Inbox, now);
        }

        if (channels.HasFlag(NotificationChannels.Email))
        {
            notification.AddDelivery(ids.NewId(), NotificationChannels.Email, now);
        }

        if (channels.HasFlag(NotificationChannels.Push))
        {
            notification.AddDelivery(ids.NewId(), NotificationChannels.Push, now);
        }

        notifications.Add(notification);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
