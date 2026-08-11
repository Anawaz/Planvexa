namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.Modules.Tenancy.Domain;

/// <summary>
/// Lists the Workspaces the authenticated user actually belongs to. This is a bootstrap query — it
/// runs with no ambient Workspace (AGENTS.md rule 6: only bootstrap endpoints may run before a
/// Workspace is selected) and is scoped purely by the caller's own memberships, backed by the
/// user-scoped RLS bootstrap read policies (0026).
/// </summary>
public sealed class WorkspaceService(
    IWorkspaceStore workspaces,
    IMembershipStore memberships)
{
    public async Task<IReadOnlyList<WorkspaceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var workspaceIds = await memberships.ListWorkspaceIdsForUserAsync(userId, cancellationToken);
        var result = new List<WorkspaceDto>(workspaceIds.Count);
        foreach (var workspaceId in workspaceIds)
        {
            if (await workspaces.FindByIdAsync(workspaceId, cancellationToken) is not { } workspace)
            {
                continue;
            }

            // The caller's own role in this workspace — the frontend workspace switcher needs it to
            // decide what to show/enable before a Workspace is even selected as ambient context.
            var membership = await memberships.FindAsync(workspaceId, userId, cancellationToken);
            result.Add(ToDto(workspace, membership?.Role.ToString() ?? "Member"));
        }

        return result;
    }

    private static WorkspaceDto ToDto(Workspace w, string role)
        => new(w.Id, w.Name, w.Slug, w.Status.ToString(), w.CreatedAtUtc, role);
}
