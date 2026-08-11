namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.Modules.Tenancy.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Implements the cross-module <see cref="IWorkspaceRosterQuery"/> for roster fan-out (e.g. the missing-time reminder scheduler).</summary>
public sealed class WorkspaceRosterQuery(IMembershipStore memberships) : IWorkspaceRosterQuery
{
    public async Task<IReadOnlyList<Guid>> ListActiveMemberUserIdsAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var all = await memberships.ListByWorkspaceAsync(workspaceId, cancellationToken);
        return all
            .Where(m => m.Status == MembershipStatus.Active && m.Role >= MembershipRole.Member)
            .Select(m => m.UserId)
            .ToList();
    }
}
