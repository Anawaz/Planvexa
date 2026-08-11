namespace Planvexa.Modules.Integrations.Application.Services;

using System.Text.Json;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Domain;
using Planvexa.SharedContracts.Events;
using Planvexa.SharedContracts.Integrations;

/// <summary>
/// Delivers workspace events to active webhook subscriptions (implements <see cref="IWebhookDispatcher"/>).
/// Runs under an ambient workspace already set by the host event pipeline. For each active subscription to
/// the event type it signs a JSON payload with the subscription secret (HMAC-SHA256) and sends it via
/// <see cref="IWebhookSender"/> (host-provided), recording a <see cref="WebhookDelivery"/> idempotently
/// on (subscription, event id).
/// </summary>
public sealed class WebhookDispatcher(
    IIdGenerator ids,
    IClock clock,
    IWebhookSubscriptionStore subscriptions,
    IWebhookDeliveryStore deliveries,
    IWebhookSender sender,
    IUnitOfWork unitOfWork) : IWebhookDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Documented receiver-side tolerance window for the <c>t=...,v1=...</c> signature scheme (see
    /// <see cref="SecretCrypto.SignWithTimestamp"/>): a receiver should reject a delivery whose <c>t</c> is
    /// more than this many seconds away from its own clock, even when <c>v1</c> is a valid signature.
    /// </summary>
    public const int ReplayToleranceSeconds = 300;

    public async Task DispatchAsync(WorkspaceEvent workspaceEvent, CancellationToken cancellationToken = default)
    {
        var workspaceId = workspaceEvent.WorkspaceId;

        var active = await subscriptions.ListActiveForEventAsync(workspaceId, workspaceEvent.EventType, cancellationToken);
        if (active.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            eventId = workspaceEvent.EventId,
            eventType = workspaceEvent.EventType,
            workspaceId = workspaceEvent.WorkspaceId,
            entityType = workspaceEvent.EntityType,
            entityId = workspaceEvent.EntityId,
            actorUserId = workspaceEvent.ActorUserId,
            data = workspaceEvent.Data,
            occurredAtUtc = clock.UtcNow,
        }, JsonOptions);

        var recorded = false;
        foreach (var subscription in active)
        {
            // Idempotency: a given event is delivered to a subscription at most once.
            if (await deliveries.ExistsAsync(subscription.Id, workspaceEvent.EventId, cancellationToken))
            {
                continue;
            }

            var signature = SecretCrypto.SignWithTimestamp(subscription.Secret, payload, clock.UtcNow);

            WebhookSendResult result;
            try
            {
                result = await sender.SendAsync(subscription.Url, payload, signature, cancellationToken);
            }
            catch (Exception ex)
            {
                result = new WebhookSendResult(false, null, ex.Message);
            }

            deliveries.Add(WebhookDelivery.Record(
                ids.NewId(), workspaceId, subscription.Id, workspaceEvent.EventId, workspaceEvent.EventType,
                attempt: 1, result.Success, result.StatusCode, result.Error, clock.UtcNow, payload));
            recorded = true;
        }

        if (recorded)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> SendAdHocAsync(Guid workspaceId, string url, string payload, CancellationToken cancellationToken = default)
    {
        // Automations "webhook" action: reuses the same HMAC signing + IWebhookSender delivery
        // path as subscribed webhooks, per the doc comment on IWebhookDispatcher.SendAdHocAsync. A fresh
        // secret is generated per call since there is no persisted WebhookSubscription to hold one.
        var secret = SecretCrypto.GenerateSecret();
        var signature = SecretCrypto.SignWithTimestamp(secret, payload, clock.UtcNow);

        try
        {
            var result = await sender.SendAsync(url, payload, signature, cancellationToken);
            return result.Success;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
