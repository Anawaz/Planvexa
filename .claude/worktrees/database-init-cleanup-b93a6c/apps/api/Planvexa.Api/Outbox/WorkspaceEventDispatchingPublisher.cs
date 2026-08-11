namespace Planvexa.Api.Outbox;

using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.BuildingBlocks.Outbox;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.SharedContracts.Automations;
using Planvexa.SharedContracts.Events;
using Planvexa.SharedContracts.Integrations;

/// <summary>
/// The outbox <see cref="IIntegrationEventPublisher"/>. It logs the event
/// and, when the message is a known task event, dispatches the derived <see cref="WorkspaceEvent"/> to the
/// automation + webhook subsystems. Each dispatch runs in its own DI scope with the ambient Workspace bound
/// to the event's workspace, so module stores/RLS isolate correctly. Idempotency is enforced downstream
/// (automation runs on (rule,event); webhook deliveries on (subscription,event)), which keeps outbox
/// re-delivery safe.
/// </summary>
public sealed class WorkspaceEventDispatchingPublisher(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkspaceEventDispatchingPublisher> logger) : IIntegrationEventPublisher
{
    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Publishing integration event {Type} ({EventId}) for workspace {WorkspaceId}",
            message.Type, message.Id, message.WorkspaceId);

        var workspaceEvent = WorkspaceEventTranslator.Translate(message);
        if (workspaceEvent is null)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();

        // Bind the ambient Workspace to the event's workspace so module queries + RLS isolate correctly.
        // The actor is the SYSTEM actor: any task writes an automation performs are attributed to the
        // system so the events they raise carry the system actor and are not re-dispatched (loop guard
        // below).
        var accessor = scope.ServiceProvider.GetRequiredService<IWorkspaceContextAccessor>();
        accessor.Set(new WorkspaceContext(
            workspaceId: workspaceEvent.WorkspaceId,
            userId: PlatformActors.System,
            membershipId: null,
            role: string.Empty,
            permissions: new HashSet<string>(),
            entitlements: new HashSet<string>(),
            correlationId: message.CorrelationId ?? Guid.CreateVersion7().ToString()));

        // Also stamp ICurrentUser (not just IWorkspaceContextAccessor) to the system actor.
        // WorkspaceConnectionInterceptor's app.current_user RLS session variable is driven by ICurrentUser,
        // not IWorkspaceContextAccessor — leaving it unset meant this scope's freshly-constructed
        // (unauthenticated-by-default) CurrentUser drove that session variable, rather than a well-defined
        // system identity. Every read the dispatch pipeline previously performed had an EF-side workspace
        // filter as a redundant backup that happened to mask the gap — the email action is the
        // first read (workspace membership) that depends on RLS alone (Workspace/WorkspaceMember carry no
        // EF-side filter; see WorkspaceStore.FindByIdAsync's doc comment), which is what surfaced it.
        scope.ServiceProvider.GetRequiredService<CurrentUser>().Set(
            PlatformActors.System, "system", "system@planvexa.local", "System");

        var automations = scope.ServiceProvider.GetRequiredService<IAutomationDispatcher>();
        var webhooks = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();

        await automations.DispatchAsync(workspaceEvent, cancellationToken);
        await webhooks.DispatchAsync(workspaceEvent, cancellationToken);
    }
}
