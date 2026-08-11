namespace Planvexa.SharedContracts.Mobile;

/// <summary>
/// Contract (implemented in Infrastructure) that lets the Notifications module check whether a user has
/// at least one registered push-capable device, without touching the Mobile module's tables directly
/// (AGENTS.md rule 7). Used by <c>NotificationDeliveryProcessor</c> to decide whether a Push delivery is
/// eligible before invoking <c>IPushSender</c>.
/// </summary>
public interface IPushDeviceDirectory
{
    Task<bool> HasActiveDeviceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Web Push subscriptions (endpoint/p256dh/auth) for a user's devices in a workspace, used by
    /// <c>WebPushSender</c> (RFC 8291/8292) to actually address and encrypt a push. Devices without a
    /// stored subscription (native apps that never called <c>PushManager.subscribe()</c>, or platforms not
    /// yet wired to FCM/APNs) are omitted rather than returned with null fields.
    /// </summary>
    Task<IReadOnlyList<PushSubscription>> ListSubscriptionsAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears a device's stored push subscription after the push service responds 404/410 (the endpoint is
    /// gone for good per the Web Push protocol). The device row itself is kept -- only the now-stale
    /// endpoint/p256dh/auth are cleared -- so re-subscribing (a fresh <c>POST /mobile/devices</c>) updates
    /// the same row via <c>DeviceService.RegisterAsync</c>'s token-hash lookup.
    /// </summary>
    Task MarkPushSubscriptionExpiredAsync(Guid deviceId, CancellationToken cancellationToken = default);
}

/// <summary>A browser PushSubscription's addressing/encryption fields, as stored on a DeviceRegistration.</summary>
public sealed record PushSubscription(Guid DeviceId, string Endpoint, string P256dh, string Auth);
