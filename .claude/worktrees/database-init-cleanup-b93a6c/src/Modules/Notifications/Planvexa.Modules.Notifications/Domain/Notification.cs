namespace Planvexa.Modules.Notifications.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.SharedContracts.Notifications;

public enum DeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Suppressed = 3,
}

/// <summary>
/// A durable notification for a recipient (the inbox is these rows). Deduplicated per workspace +
/// recipient + <see cref="DeduplicationKey"/> so re-raising the same event is a safe no-op.
/// </summary>
public sealed class Notification : Entity, IAggregateRoot, IWorkspaceOwned
{
    private readonly List<NotificationDelivery> _deliveries = new();

    private Notification()
    {
    }

    private Notification(
        Guid id, Guid workspaceId, Guid recipientUserId, string eventType,
        string entityType, Guid entityId, string? payload, string deduplicationKey, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        RecipientUserId = recipientUserId;
        EventType = eventType;
        EntityType = entityType;
        EntityId = entityId;
        Payload = payload;
        DeduplicationKey = deduplicationKey;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string? Payload { get; private set; }
    public string DeduplicationKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }

    public IReadOnlyList<NotificationDelivery> Deliveries => _deliveries.AsReadOnly();

    public static Notification Create(
        Guid id, Guid workspaceId, Guid recipientUserId, string eventType,
        string entityType, Guid entityId, string? payload, string deduplicationKey, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(recipientUserId, nameof(recipientUserId));
        Guard.AgainstNullOrWhiteSpace(eventType, nameof(eventType));
        Guard.AgainstNullOrWhiteSpace(deduplicationKey, nameof(deduplicationKey));
        return new Notification(id, workspaceId, recipientUserId, eventType, entityType, entityId, payload, deduplicationKey, nowUtc);
    }

    /// <summary>Enqueues a delivery on a channel. Inbox is delivered immediately (the row itself).</summary>
    public NotificationDelivery AddDelivery(Guid id, NotificationChannels channel, DateTimeOffset nowUtc)
    {
        var status = channel == NotificationChannels.Inbox ? DeliveryStatus.Sent : DeliveryStatus.Pending;
        var delivery = new NotificationDelivery(id, Id, channel, status, nowUtc);
        _deliveries.Add(delivery);
        return delivery;
    }

    public void MarkRead(DateTimeOffset nowUtc)
    {
        ReadAtUtc ??= nowUtc;
    }
}

/// <summary>A single channel delivery attempt for a notification. Drained idempotently by a worker.</summary>
public sealed class NotificationDelivery : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private NotificationDelivery()
    {
    }

    public NotificationDelivery(Guid id, Guid notificationId, NotificationChannels channel, DeliveryStatus status, DateTimeOffset nowUtc)
        : base(id)
    {
        NotificationId = notificationId;
        Channel = channel;
        Status = status;
        CreatedAtUtc = nowUtc;
        if (status == DeliveryStatus.Sent)
        {
            SentAtUtc = nowUtc;
        }
    }

    public Guid NotificationId { get; private set; }
    public NotificationChannels Channel { get; private set; }
    public DeliveryStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }

    public void MarkSent(DateTimeOffset nowUtc)
    {
        Status = DeliveryStatus.Sent;
        SentAtUtc = nowUtc;
        Error = null;
    }

    public void MarkFailed(string error)
    {
        Attempts += 1;
        Status = Attempts >= 5 ? DeliveryStatus.Failed : DeliveryStatus.Pending;
        Error = error;
    }

    /// <summary>
    /// Terminal, non-retried outcome for a delivery that is deterministically not eligible right now
    /// (e.g. Push with no registered device) rather than a transient failure. A future retry loop would
    /// just get the same answer, so this does not consume an attempt or route through <see cref="MarkFailed"/>.
    /// </summary>
    public void MarkSuppressed(string reason)
    {
        Status = DeliveryStatus.Suppressed;
        Error = reason;
    }
}

/// <summary>Per-user, per-event-type channel preferences. Absence implies the sensible default.</summary>
public sealed class NotificationPreference : Entity, IWorkspaceOwned
{
    private NotificationPreference()
    {
    }

    private NotificationPreference(Guid id, Guid workspaceId, Guid userId, string eventType, bool inbox, bool email, bool push)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        EventType = eventType;
        Inbox = inbox;
        Email = email;
        Push = push;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public bool Inbox { get; private set; }
    public bool Email { get; private set; }
    public bool Push { get; private set; }

    public static NotificationPreference Create(Guid id, Guid workspaceId, Guid userId, string eventType, bool inbox, bool email, bool push = false)
    {
        Guard.AgainstNullOrWhiteSpace(eventType, nameof(eventType));
        return new NotificationPreference(id, workspaceId, userId, eventType, inbox, email, push);
    }

    public void Update(bool inbox, bool email, bool push)
    {
        Inbox = inbox;
        Email = email;
        Push = push;
    }
}

/// <summary>Cadence for a user's per-workspace activity digest email. Off means no digest is sent.</summary>
public enum DigestFrequency
{
    Off = 0,
    Daily = 1,
    Weekly = 2,
}

/// <summary>
/// A user's digest cadence for one workspace, distinct from the per-event-type <see cref="NotificationPreference"/>
/// (digest cadence is a single global-per-workspace setting, not scoped to an event type).
/// <see cref="LastSentAtUtc"/> is the bookkeeping the scheduler uses to decide when the next digest is due.
/// </summary>
public sealed class DigestPreference : Entity, IWorkspaceOwned
{
    private DigestPreference()
    {
    }

    private DigestPreference(Guid id, Guid workspaceId, Guid userId, DigestFrequency frequency, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        Frequency = frequency;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public DigestFrequency Frequency { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? LastSentAtUtc { get; private set; }

    public static DigestPreference Create(Guid id, Guid workspaceId, Guid userId, DigestFrequency frequency, DateTimeOffset nowUtc)
        => new(id, workspaceId, userId, frequency, nowUtc);

    public void SetFrequency(DigestFrequency frequency) => Frequency = frequency;

    public void MarkSent(DateTimeOffset nowUtc) => LastSentAtUtc = nowUtc;

    /// <summary>True when this preference is due for a run: enabled and the cadence interval has elapsed.</summary>
    public bool IsDue(DateTimeOffset nowUtc)
    {
        if (Frequency == DigestFrequency.Off)
        {
            return false;
        }

        var interval = Frequency == DigestFrequency.Daily ? TimeSpan.FromHours(24) : TimeSpan.FromDays(7);
        var last = LastSentAtUtc ?? CreatedAtUtc;
        return nowUtc - last >= interval;
    }
}
