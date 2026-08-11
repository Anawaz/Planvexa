namespace Planvexa.SharedContracts.Integrations;

using Planvexa.SharedContracts.Events;

/// <summary>
/// Contract (implemented by the Integrations module) invoked by the host event pipeline for each
/// <see cref="WorkspaceEvent"/>. The dispatcher runs under an ambient tenant already set to the event's
/// tenant. It finds active webhook subscriptions for the workspace + event type and delivers a signed
/// HTTP request, recording a delivery idempotently on (subscription, event id).
/// </summary>
public interface IWebhookDispatcher
{
    Task DispatchAsync(WorkspaceEvent workspaceEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Automations "webhook" action: sends a one-off signed POST to an admin-configured URL
    /// that is NOT backed by a persisted <see cref="Planvexa.SharedContracts.Events.WorkspaceEvent"/>
    /// subscription. Reuses the same signing (HMAC-SHA256 with a freshly generated per-call secret,
    /// included in the response so the receiver can verify) and delivery mechanism
    /// (<see cref="IWebhookSender"/>) as subscribed webhooks, per AGENTS.md rule 16 (prefer existing
    /// capabilities). Not recorded in <c>WebhookDelivery</c> (that table is subscription-scoped) — the
    /// calling automation's own run history records success/failure instead. Returns true on a successful
    /// (2xx) delivery.
    /// </summary>
    Task<bool> SendAdHocAsync(Guid workspaceId, string url, string payload, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends a signed HTTP POST to a webhook endpoint. Implemented in the composition root (API host) with
/// an <c>HttpClient</c>; abstracted so the Integrations module stays free of HTTP hosting concerns and
/// can be unit-tested with a fake sender.
/// </summary>
public interface IWebhookSender
{
    /// <summary>Posts the payload with an HMAC-SHA256 signature header. Returns the outcome.</summary>
    Task<WebhookSendResult> SendAsync(string url, string payload, string signature, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a webhook send attempt.</summary>
public sealed record WebhookSendResult(bool Success, int? StatusCode, string? Error);
