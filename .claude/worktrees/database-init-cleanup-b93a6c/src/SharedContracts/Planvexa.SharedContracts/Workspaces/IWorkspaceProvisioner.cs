namespace Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Cross-module contract (implemented by the WorkManagement module) that provisions a newly-created
/// Workspace's default starter structure — a General Space, a starter List, and the default status
/// scheme — so the Workspace is immediately usable. Called by the Tenancy module inside the
/// Workspace-creation transaction. Implementations MUST be idempotent (a Workspace that already has
/// content is left untouched) and MUST NOT save; the caller commits the surrounding unit of work.
/// </summary>
public interface IWorkspaceProvisioner
{
    Task ProvisionDefaultsAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
