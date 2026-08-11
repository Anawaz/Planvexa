namespace Planvexa.Modules.Integrations.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Events;

/// <summary>
/// A workspace subscription to workspace events, delivered as signed HTTP POSTs to <see cref="Url"/>.
/// The signing secret is stored in the clear (it is a shared HMAC key, not a password) and is returned
/// only once at creation. Subscribed event types are stored as a comma-separated list.
/// </summary>
public sealed class WebhookSubscription : Entity, IAggregateRoot, IWorkspaceOwned
{
    private WebhookSubscription()
    {
    }

    private WebhookSubscription(
        Guid id, Guid workspaceId, string url, string secret, string eventTypesCsv,
        Guid createdBy, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Url = url;
        Secret = secret;
        EventTypesCsv = eventTypesCsv;
        IsActive = true;
        CreatedByUserId = createdBy;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string Secret { get; private set; } = string.Empty;
    public string EventTypesCsv { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<string> EventTypes =>
        EventTypesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool IsSubscribedTo(string eventType) => EventTypes.Contains(eventType);

    public static WebhookSubscription Create(
        Guid id, Guid workspaceId, string url, IReadOnlyCollection<string> eventTypes,
        Guid createdBy, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(url, nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ValidationAppException("Webhook URL must be an absolute http(s) URL.");
        }

        var valid = eventTypes.Where(e => WorkspaceEvent.Types.All.Contains(e)).Distinct().ToList();
        if (valid.Count == 0)
        {
            throw new ValidationAppException("At least one valid event type is required.");
        }

        var secret = SecretCrypto.GenerateSecret();
        return new WebhookSubscription(id, workspaceId, url.Trim(), secret, string.Join(',', valid), createdBy, nowUtc);
    }

    public void Deactivate() => IsActive = false;
}

/// <summary>An immutable record of one webhook delivery attempt (idempotent per subscription + event).</summary>
public sealed class WebhookDelivery : Entity, IWorkspaceOwned
{
    private WebhookDelivery()
    {
    }

    private WebhookDelivery(
        Guid id, Guid workspaceId, Guid subscriptionId, Guid eventId, string eventType,
        int attempt, bool success, int? statusCode, string? detail, DateTimeOffset occurredAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        SubscriptionId = subscriptionId;
        EventId = eventId;
        EventType = eventType;
        Attempt = attempt;
        Success = success;
        StatusCode = statusCode;
        Detail = detail;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public int Attempt { get; private set; }
    public bool Success { get; private set; }
    public int? StatusCode { get; private set; }
    public string? Detail { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static WebhookDelivery Record(
        Guid id, Guid workspaceId, Guid subscriptionId, Guid eventId, string eventType,
        int attempt, bool success, int? statusCode, string? detail, DateTimeOffset occurredAtUtc)
        => new(id, workspaceId, subscriptionId, eventId, eventType, attempt, success, statusCode,
            detail is { Length: > 500 } ? detail[..500] : detail, occurredAtUtc);
}
