namespace Planvexa.Infrastructure.Workspaces;

using Microsoft.EntityFrameworkCore;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.Governance.Domain;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Domain;

/// <summary>
/// Implements Workspace resolution. This runs WITHOUT an ambient Workspace (the global query filter
/// is a no-op) and filters explicitly by the correct keys, backed by the user-scoped RLS bootstrap
/// read policies (0020/0026).
/// </summary>
internal sealed class WorkspaceResolver(PlanvexaDbContext db) : IWorkspaceResolver
{
    public async Task<WorkspaceResolution?> ResolveByWorkspaceIdAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
    {
        var workspace = await db.Workspaces.IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.Id == workspaceId && w.Status == WorkspaceStatus.Active, cancellationToken);

        if (workspace is null)
        {
            return null;
        }

        var role = await db.WorkspaceMembers.IgnoreQueryFilters()
            .Where(m => m.WorkspaceId == workspaceId
                && m.UserId == userId
                && m.Status == MembershipStatus.Active)
            .Select(m => (MembershipRole?)m.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (role is null)
        {
            return null;
        }

        var features = await db.FeatureEntitlements.IgnoreQueryFilters()
            .Where(f => f.WorkspaceId == workspaceId && f.IsEnabled)
            .Select(f => f.FeatureKey)
            .ToListAsync(cancellationToken);

        // No settings row = MfaRequired defaults to false (EnterpriseSecuritySettings.CreateDefault's
        // default, mirrored here by FirstOrDefaultAsync on a bool projection rather than loading the
        // whole settings row just to check one flag).
        var requiresMfa = await db.Set<EnterpriseSecuritySettings>().IgnoreQueryFilters()
            .Where(s => s.WorkspaceId == workspaceId)
            .Select(s => s.MfaRequired)
            .FirstOrDefaultAsync(cancellationToken);

        return new WorkspaceResolution(workspace.Id, workspace.Slug, role.Value, features.ToHashSet(), requiresMfa);
    }

    public async Task<bool> CanAccessWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        return await db.WorkspaceMembers.IgnoreQueryFilters().AnyAsync(
            m => m.WorkspaceId == workspaceId
                && m.UserId == userId
                && m.Status == MembershipStatus.Active,
            cancellationToken);
    }
}
