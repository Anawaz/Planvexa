namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Provisions a new Workspace's starter structure (default status scheme, a General Space, a Tasks
/// List). Idempotent: does nothing when the Workspace already has a Space. Adds entities to the unit
/// of work but does not save — the Tenancy Workspace-creation transaction commits everything.
/// </summary>
public sealed class WorkspaceDefaultsProvisioner(
    IWorkspaceContextAccessor workspaceAccessor,
    ISpaceStore spaces,
    ITaskListStore lists,
    WorkspaceProvisioningService schemeProvisioning,
    IIdGenerator ids,
    IClock clock) : IWorkspaceProvisioner
{
    private const double FirstPosition = 1024d;

    public async Task ProvisionDefaultsAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var context = workspaceAccessor.Current;
        var createdBy = context.UserId;
        var now = clock.UtcNow;

        var existing = await spaces.ListByWorkspaceAsync(workspaceId, cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var scheme = await schemeProvisioning.EnsureDefaultSchemeAsync(workspaceId, cancellationToken);

        var space = Space.Create(ids.NewId(), workspaceId, "General", FirstPosition, createdBy, now);
        spaces.Add(space);

        var list = TaskList.Create(
            ids.NewId(), workspaceId, space.Id, folderId: null, "Tasks", scheme.Id, FirstPosition, createdBy, now);
        lists.Add(list);
    }
}
