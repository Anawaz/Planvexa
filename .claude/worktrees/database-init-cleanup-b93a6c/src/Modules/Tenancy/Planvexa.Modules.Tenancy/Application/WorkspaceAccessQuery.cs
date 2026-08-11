namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.Modules.Tenancy.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Implements the cross-module <see cref="IWorkspaceAccessQuery"/>. Access is based only on the user's
/// direct active membership in the workspace.
/// </summary>
public sealed class WorkspaceAccessQuery(IMembershipStore memberships) : IWorkspaceAccessQuery
{
    public async Task<WorkspaceAccess?> GetAccessAsync(
        Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
    {
        // No separate "does the workspace exist" pre-check: a membership row cannot outlive its
        // workspace (workspace_members.workspace_id FK is ON DELETE CASCADE — see
        // 0030_DropTenantColumnsAndTable.sql), so a missing/deleted workspace already implies no
        // membership row, and the check below covers it. A prior version of this method DID probe
        // tenancy.workspaces first via IWorkspaceStore.FindByIdAsync — but that table's ONLY SELECT RLS
        // policy (bootstrap_workspace_read, 0026) requires the AMBIENT app.current_user to itself be an
        // active member of the target workspace. That's correct for the "list my own workspaces"
        // bootstrap flow it was built for, but wrong here: this query is asked "is <userId> a member of
        // <workspaceId>?" from callers running under a DIFFERENT ambient identity than userId — most
        // notably every automation action, which always runs as the system actor (see
        // AutomationDispatcher's class doc comment). The system actor is never itself a workspace
        // member, so that pre-check made GetAccessAsync return null unconditionally whenever it was
        // asked about anyone from a system-actor scope, even a genuinely active member. The membership
        // lookup itself (IMembershipStore.FindAsync) has no such gap: its RLS policy (workspace_isolation)
        // is scoped purely by workspace_id = the ambient app.current_workspace, with no dependency on
        // which user is ambient.
        var direct = await memberships.FindAsync(workspaceId, userId, cancellationToken);
        var effective = direct?.Role;
        if (effective is null)
        {
            return null;
        }

        return new WorkspaceAccess(
            workspaceId, userId, (WorkspaceRole)(int)effective.Value, effective == MembershipRole.Guest);
    }
}
