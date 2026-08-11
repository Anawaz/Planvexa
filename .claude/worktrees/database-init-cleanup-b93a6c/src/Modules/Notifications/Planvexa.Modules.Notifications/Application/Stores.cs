namespace Planvexa.Modules.Notifications.Application;

using Planvexa.Modules.Notifications.Domain;

public interface INotificationStore
{
    void Add(Notification notification);
    Task<Notification?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid workspaceId, Guid recipientUserId, string deduplicationKey, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> ListForRecipientAsync(Guid workspaceId, Guid recipientUserId, bool unreadOnly, int max, CancellationToken ct = default);
    Task<int> UnreadCountAsync(Guid workspaceId, Guid recipientUserId, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> ListUnreadForMarkAllAsync(Guid workspaceId, Guid recipientUserId, CancellationToken ct = default);
}

public interface INotificationPreferenceStore
{
    void Add(NotificationPreference preference);
    Task<NotificationPreference?> FindAsync(Guid workspaceId, Guid userId, string eventType, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationPreference>> ListForUserAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}

public interface INotificationDeliveryStore
{
    Task<IReadOnlyList<NotificationDelivery>> ListPendingAsync(int max, CancellationToken ct = default);
    Task<Notification?> FindByDeliveryAsync(Guid notificationId, CancellationToken ct = default);
}

/// <summary>Sends an email for a delivered notification. Dev implementation logs / targets Mailpit.</summary>
public interface IEmailSender
{
    Task SendAsync(Guid recipientUserId, string subject, string body, CancellationToken ct = default);
}

/// <summary>
/// Sends a push notification for a delivered notification. Mirrors <see cref="IEmailSender"/>'s shape so
/// the delivery processor treats channels uniformly. The shipped dev implementation
/// (<c>LoggingPushSender</c> in the API host) is log-only — see its doc comment for what a real
/// implementation (Web Push/VAPID or FCM/APNs) needs that this codebase does not yet have.
/// </summary>
public interface IPushSender
{
    Task SendAsync(Guid recipientUserId, string title, string body, CancellationToken ct = default);
}

public interface IDigestPreferenceStore
{
    void Add(DigestPreference preference);
    Task<DigestPreference?> FindAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>Cross-workspace read for the digest scheduler (mirrors IRetentionPolicyStore.ListAllAsync).</summary>
    Task<IReadOnlyList<DigestPreference>> ListEnabledAsync(CancellationToken ct = default);
}
