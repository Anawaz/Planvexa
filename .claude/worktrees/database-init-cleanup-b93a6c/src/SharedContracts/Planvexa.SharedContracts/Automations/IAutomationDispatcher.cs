namespace Planvexa.SharedContracts.Automations;

using Planvexa.SharedContracts.Events;

/// <summary>
/// Contract (implemented by the Automations module) invoked by the host event pipeline for each
/// <see cref="WorkspaceEvent"/>. The dispatcher runs under an ambient tenant already set to the event's
/// tenant. It matches enabled rules, evaluates conditions, executes actions and records runs
/// idempotently on (rule, event id).
/// </summary>
public interface IAutomationDispatcher
{
    Task DispatchAsync(WorkspaceEvent workspaceEvent, CancellationToken cancellationToken = default);
}
