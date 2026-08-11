namespace Planvexa.Modules.Integrations.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Authorization;
using Planvexa.Modules.Integrations.Domain;
using Planvexa.SharedContracts.Integrations;

/// <summary>
/// Manages webhook subscriptions and their delivery logs (Admin+). Unlike Automations (which has a
/// background retry-with-backoff sweep — see AutomationRetryBackgroundService), a failed webhook delivery
/// is not automatically retried; <see cref="RetryDeliveryAsync"/> lets an Admin+ replay it on demand.
/// </summary>
public sealed class WebhookService(
    IntegrationsServiceContext ctx,
    IWebhookSubscriptionStore subscriptions,
    IWebhookDeliveryStore deliveries,
    IWebhookSender sender)
    : IntegrationsServiceBase(ctx)
{
    public async Task<IReadOnlyList<WebhookDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureManageWebhooks((await AccessAsync(workspaceId, ct))?.Role);

        var list = await subscriptions.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<CreatedWebhookDto> CreateAsync(CreateWebhookCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureManageWebhooks((await AccessAsync(workspaceId, ct))?.Role);

        var subscription = WebhookSubscription.Create(NewId(), workspaceId, command.Url, command.EventTypes, UserId, Now);
        subscriptions.Add(subscription);
        Audit("integrations.webhook.created", "WebhookSubscription", subscription.Id, new { subscription.Url, subscription.EventTypesCsv });
        await SaveAsync(ct);

        // The signing secret is returned exactly once.
        return new CreatedWebhookDto(subscription.Id, subscription.Url, subscription.EventTypes, subscription.IsActive, subscription.CreatedAtUtc, subscription.Secret);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureManageWebhooks((await AccessAsync(workspaceId, ct))?.Role);

        var subscription = await subscriptions.FindAsync(id, ct)
            ?? throw new NotFoundException("Webhook not found.");
        if (subscription.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Webhook not found in this workspace.");
        }

        subscription.Deactivate();
        Audit("integrations.webhook.deleted", "WebhookSubscription", id);
        await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<WebhookDeliveryDto>> ListDeliveriesAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureManageWebhooks((await AccessAsync(workspaceId, ct))?.Role);

        var subscription = await subscriptions.FindAsync(id, ct)
            ?? throw new NotFoundException("Webhook not found.");
        if (subscription.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Webhook not found in this workspace.");
        }

        var list = await deliveries.ListBySubscriptionAsync(id, 100, ct);
        return list
            .Select(d => new WebhookDeliveryDto(d.Id, d.EventType, d.Attempt, d.Success, d.StatusCode, d.Detail, d.OccurredAtUtc))
            .ToList();
    }

    /// <summary>Manually re-attempts a previously logged delivery: re-signs and resends the exact payload
    /// that was originally attempted, then overwrites the delivery row with the new outcome (Admin+).</summary>
    public async Task<WebhookDeliveryDto> RetryDeliveryAsync(Guid subscriptionId, Guid deliveryId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureManageWebhooks((await AccessAsync(workspaceId, ct))?.Role);

        var subscription = await subscriptions.FindAsync(subscriptionId, ct)
            ?? throw new NotFoundException("Webhook not found.");
        if (subscription.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Webhook not found in this workspace.");
        }

        var delivery = await deliveries.FindAsync(deliveryId, ct)
            ?? throw new NotFoundException("Delivery not found.");
        if (delivery.SubscriptionId != subscriptionId)
        {
            throw new NotFoundException("Delivery not found for this webhook.");
        }

        if (delivery.PayloadJson is null)
        {
            throw new ValidationAppException("This delivery predates retry support and cannot be replayed.");
        }

        var signature = Domain.SecretCrypto.SignWithTimestamp(subscription.Secret, delivery.PayloadJson, Now);
        WebhookSendResult result;
        try
        {
            result = await sender.SendAsync(subscription.Url, delivery.PayloadJson, signature, ct);
        }
        catch (Exception ex)
        {
            result = new WebhookSendResult(false, null, ex.Message);
        }

        delivery.ApplyRetryOutcome(result.Success, result.StatusCode, result.Error, Now);
        Audit("integrations.webhook.delivery_retried", "WebhookDelivery", delivery.Id, new { subscriptionId, delivery.EventId, result.Success });
        await SaveAsync(ct);

        return new WebhookDeliveryDto(delivery.Id, delivery.EventType, delivery.Attempt, delivery.Success, delivery.StatusCode, delivery.Detail, delivery.OccurredAtUtc);
    }

    private static WebhookDto ToDto(WebhookSubscription s)
        => new(s.Id, s.Url, s.EventTypes, s.IsActive, s.CreatedAtUtc);
}
