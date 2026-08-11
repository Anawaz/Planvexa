namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Tenancy.Authorization;
using Planvexa.SharedContracts.Search;
using Planvexa.SharedContracts.Users;

/// <summary>
/// Cross-module search: workspace members and teams. Member-directory visibility is a
/// workspace-wide category gate, not a per-row ACL (<c>members.view</c> is simply absent from
/// the Guest/Limited-Member built-in role grants — see BuiltInRoles and MembershipService, which this
/// mirrors exactly). A caller without <c>members.view</c> gets zero Member/Team results, not a filtered
/// subset — see ISearchProvider's doc comment on why this filter is not optional.
/// </summary>
public sealed class TenancySearchProvider(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    IMembershipStore memberships,
    ITeamStore teams,
    IRolePermissionResolver roleResolver,
    IUserDirectory users) : ISearchProvider
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default)
    {
        var workspace = workspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return [];
        }

        var caller = await memberships.FindAsync(workspace.WorkspaceId, currentUser.UserId, cancellationToken);
        var permissions = await roleResolver.ResolveAsync(caller, cancellationToken);
        if (!TenancyAuthorizer.Can(permissions, TenancyPermissions.MembersView))
        {
            return [];
        }

        var hits = new List<SearchHit>();

        var teamMatches = (await teams.ListByWorkspaceAsync(workspace.WorkspaceId, cancellationToken))
            .Where(t => !t.IsArchived && t.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(t => new SearchHit("Team", t.Id, t.Name, "Team", null));
        hits.AddRange(teamMatches);

        if (hits.Count < limit)
        {
            // ponytail: one IUserDirectory lookup per member — bounded by a workspace's member count,
            // which the member directory itself already lists in full for members.view holders (see
            // MembershipService.ListWorkspaceMembersAsync). Upgrade to a batch IUserDirectory lookup if
            // workspaces routinely grow into the thousands of members.
            var members = await memberships.ListByWorkspaceAsync(workspace.WorkspaceId, cancellationToken);
            foreach (var member in members)
            {
                if (hits.Count >= limit)
                {
                    break;
                }

                var user = await users.FindByIdAsync(member.UserId, cancellationToken);
                if (user is not null && user.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new SearchHit("Member", member.UserId, user.DisplayName, member.Role.ToString(), null));
                }
            }
        }

        return hits;
    }
}
