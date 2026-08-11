namespace Planvexa.Modules.Integrations.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Authorization;
using Planvexa.Modules.Integrations.Domain;

/// <summary>Manages webhook subscriptions and their delivery logs (Admin+).</summary>
public sealed class WebhookService(
    IntegrationsServiceContext ctx,
    IWebhookSubscriptionStore subscriptions,
    IWebhookDeliveryStore deliveries)
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

    private static WebhookDto ToDto(WebhookSubscription s)
        => new(s.Id, s.Url, s.EventTypes, s.IsActive, s.CreatedAtUtc);
}
