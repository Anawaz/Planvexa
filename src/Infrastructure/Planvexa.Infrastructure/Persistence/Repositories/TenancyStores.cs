namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Domain;

/// <summary>See <see cref="WorkspaceStore.FindByIdAsync"/>'s doc comment for why this exists.</summary>
internal static class TenancySessionGuard
{
    public static async Task<T> WithStampedWorkspaceAsync<T>(PlanvexaDbContext db, Guid workspaceId, Func<Task<T>> read, CancellationToken ct)
    {
        // Explicitly opening (and keeping open) the connection means EF's own subsequent commands reuse
        // THIS SAME physical connection rather than each independently opening/closing against the pool —
        // guaranteeing the set_config and the read below are never split across two different connections.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.current_workspace', {workspaceId.ToString()}, false)", ct);
            return await read();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

internal sealed class WorkspaceStore(PlanvexaDbContext db) : IWorkspaceStore
{
    public void Add(Workspace workspace) => db.Workspaces.Add(workspace);

    public async Task<Workspace?> FindByIdAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        // Workspace carries no EF-side global query filter (see PlanvexaDbContext's
        // ApplyWorkspaceQueryFilters doc comment — "accessed by id/bootstrap membership rather than
        // filtered by an ambient workspace"), so visibility here depends ENTIRELY on the Postgres RLS
        // session variable being current on this connection. Every other IWorkspaceOwned read has an EF
        // C#-side filter as a redundant safety net that masks a connection-pool race where a query's
        // physical connection can differ from whichever connection the interceptor most recently stamped
        // (EF opens/closes per command; a pooled connection handed back for THIS command may carry a
        // stale/no stamp from a different logical scope until the interceptor re-fires on ITS open) —
        // Workspace has no such net, so that race is directly observable here. Explicitly holding the
        // connection open across the stamp AND the read (TenancySessionGuard) guarantees both run on the
        // SAME physical connection, closing the race outright.
        return await TenancySessionGuard.WithStampedWorkspaceAsync(
            db, workspaceId, () => db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken), cancellationToken);
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
        => db.Workspaces.AnyAsync(w => w.Slug == slug, cancellationToken);
}

internal sealed class MembershipStore(PlanvexaDbContext db) : IMembershipStore
{
    public void Add(WorkspaceMember member) => db.WorkspaceMembers.Add(member);

    public void Remove(WorkspaceMember member) => db.WorkspaceMembers.Remove(member);

    public async Task<WorkspaceMember?> FindAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
    {
        // See WorkspaceStore.FindByIdAsync's doc comment — this store's IgnoreQueryFilters() calls mean
        // EVERY read here already depends purely on RLS, with no EF-side backup filter, so the same fix applies.
        return await TenancySessionGuard.WithStampedWorkspaceAsync(
            db, workspaceId,
            () => db.WorkspaceMembers.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken),
            cancellationToken);
    }

    public Task<WorkspaceMember?> FindByIdAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default)
        => db.WorkspaceMembers.IgnoreQueryFilters().FirstOrDefaultAsync(
            m => m.WorkspaceId == workspaceId && m.Id == membershipId, cancellationToken);

    public Task<int> CountActiveOwnersAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => db.WorkspaceMembers.CountAsync(
            m => m.WorkspaceId == workspaceId
                && m.Role == MembershipRole.Owner && m.Status == MembershipStatus.Active,
            cancellationToken);

    public async Task<IReadOnlyList<WorkspaceMember>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => await db.WorkspaceMembers.IgnoreQueryFilters()
            .Where(m => m.WorkspaceId == workspaceId)
            .OrderBy(m => m.JoinedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListWorkspaceIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await db.WorkspaceMembers.IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Select(m => m.WorkspaceId)
            .Distinct()
            .ToListAsync(cancellationToken);
}

internal sealed class InvitationStore(PlanvexaDbContext db, MaintenanceConnection maintenance) : IInvitationStore
{
    public void Add(Invitation invitation) => db.Invitations.Add(invitation);

    public Task<Invitation?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => maintenance.LookupAsync(db, () =>
            db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken));

    public Task<Invitation?> FindPendingAsync(Guid workspaceId, string email, CancellationToken cancellationToken = default)
        => db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(
            i => i.WorkspaceId == workspaceId && i.Email == email && i.Status == InvitationStatus.Pending,
            cancellationToken);

    public Task<Invitation?> FindByIdAsync(Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default)
        => db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(
            i => i.WorkspaceId == workspaceId && i.Id == invitationId, cancellationToken);

    public async Task<IReadOnlyList<Invitation>> ListPendingByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => await db.Invitations.IgnoreQueryFilters()
            .Where(i => i.WorkspaceId == workspaceId && i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<bool> HasPendingForEmailAsync(string email, CancellationToken cancellationToken = default)
        => db.Invitations.IgnoreQueryFilters().AnyAsync(
            i => i.Email == email && i.Status == InvitationStatus.Pending, cancellationToken);
}

internal sealed class TeamStore(PlanvexaDbContext db) : ITeamStore
{
    public void Add(Team team) => db.Set<Team>().Add(team);

    public void Remove(Team team) => db.Set<Team>().Remove(team);

    public void AddMember(TeamMembership membership) => db.Set<TeamMembership>().Add(membership);

    public void RemoveMember(TeamMembership membership) => db.Set<TeamMembership>().Remove(membership);

    public Task<Team?> FindAsync(Guid teamId, CancellationToken cancellationToken = default)
        => db.Set<Team>().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);

    public async Task<IReadOnlyList<Team>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => await db.Set<Team>().IgnoreQueryFilters()
            .Where(t => t.WorkspaceId == workspaceId)
            .OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TeamMembership>> ListMembersAsync(Guid teamId, CancellationToken cancellationToken = default)
        => await db.Set<TeamMembership>().IgnoreQueryFilters()
            .Where(m => m.TeamId == teamId)
            .OrderBy(m => m.AddedAtUtc).ToListAsync(cancellationToken);

    public Task<TeamMembership?> FindMemberAsync(Guid teamId, Guid userId, CancellationToken cancellationToken = default)
        => db.Set<TeamMembership>().IgnoreQueryFilters().FirstOrDefaultAsync(
            m => m.TeamId == teamId && m.UserId == userId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountMembersByTeamAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var counts = await db.Set<TeamMembership>().IgnoreQueryFilters()
            .Where(m => m.WorkspaceId == workspaceId)
            .GroupBy(m => m.TeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(x => x.TeamId, x => x.Count);
    }

    public async Task<IReadOnlyList<Guid>> ListTeamIdsForUserAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
        => await db.Set<TeamMembership>().IgnoreQueryFilters()
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == userId)
            .Select(m => m.TeamId)
            .ToListAsync(cancellationToken);
}

internal sealed class ResourcePermissionStore(PlanvexaDbContext db) : IResourcePermissionStore
{
    public void Add(ResourcePermission grant) => db.Set<ResourcePermission>().Add(grant);

    public void Remove(ResourcePermission grant) => db.Set<ResourcePermission>().Remove(grant);

    public Task<ResourcePermission?> FindAsync(
        Guid workspaceId, string resourceType, Guid resourceId,
        ResourcePrincipalType principalType, Guid principalId, CancellationToken cancellationToken = default)
        => db.Set<ResourcePermission>().FirstOrDefaultAsync(
            p => p.WorkspaceId == workspaceId && p.ResourceType == resourceType && p.ResourceId == resourceId
                && p.PrincipalType == principalType && p.PrincipalId == principalId,
            cancellationToken);

    public async Task<IReadOnlyList<ResourcePermission>> ListForResourceAsync(
        Guid workspaceId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
        => await db.Set<ResourcePermission>()
            .Where(p => p.WorkspaceId == workspaceId && p.ResourceType == resourceType && p.ResourceId == resourceId)
            .ToListAsync(cancellationToken);

    public Task<bool> AnyForResourceAsync(
        Guid workspaceId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
        => db.Set<ResourcePermission>().AnyAsync(
            p => p.WorkspaceId == workspaceId && p.ResourceType == resourceType && p.ResourceId == resourceId,
            cancellationToken);

    public async Task<IReadOnlySet<Guid>> ListResourceIdsWithGrantsAsync(
        Guid workspaceId, string resourceType, IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken = default)
    {
        if (resourceIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await db.Set<ResourcePermission>()
            .Where(p => p.WorkspaceId == workspaceId && p.ResourceType == resourceType && resourceIds.Contains(p.ResourceId))
            .Select(p => p.ResourceId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }
}

internal sealed class FeatureEntitlementStore(PlanvexaDbContext db) : IFeatureEntitlementStore
{
    public void Add(FeatureEntitlement entitlement) => db.FeatureEntitlements.Add(entitlement);

    public void Remove(FeatureEntitlement entitlement) => db.FeatureEntitlements.Remove(entitlement);

    public async Task<IReadOnlyList<FeatureEntitlement>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => await db.FeatureEntitlements
            .IgnoreQueryFilters()  // Explicit filter below is the source of truth, not the ambient workspace context.
            .Where(f => f.WorkspaceId == workspaceId)
            .OrderBy(f => f.FeatureKey)
            .ToListAsync(cancellationToken);
}

internal sealed class RoleStore(PlanvexaDbContext db) : IRoleStore
{
    public void Add(Role role) => db.Roles.Add(role);

    public void AddPermission(RolePermission permission) => db.RolePermissionGrants.Add(permission);

    public Task<Role?> FindByIdAsync(Guid workspaceId, Guid roleId, CancellationToken cancellationToken = default)
        => db.Roles.FirstOrDefaultAsync(r => r.WorkspaceId == workspaceId && r.Id == roleId, cancellationToken);

    public Task<Role?> FindByKeyAsync(Guid workspaceId, string key, CancellationToken cancellationToken = default)
        => db.Roles.FirstOrDefaultAsync(r => r.WorkspaceId == workspaceId && r.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Role>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => await db.Roles
            .Where(r => r.WorkspaceId == workspaceId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> GetPermissionKeysAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var keys = await db.RolePermissionGrants
            .IgnoreQueryFilters() // resolved by RoleId alone; the role's own row already proves the workspace.
            .Where(p => p.RoleId == roleId)
            .Select(p => p.PermissionKey)
            .ToListAsync(cancellationToken);
        return keys.ToHashSet();
    }

    public async Task<IReadOnlyList<RoleWithPermissions>> ListWithPermissionsAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var roleList = await ListByWorkspaceAsync(workspaceId, cancellationToken);
        var grants = await db.RolePermissionGrants
            .Where(p => p.WorkspaceId == workspaceId)
            .ToListAsync(cancellationToken);
        var byRole = grants
            .GroupBy(p => p.RoleId)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(p => p.PermissionKey).ToHashSet());

        return roleList
            .Select(r => new RoleWithPermissions(r, byRole.TryGetValue(r.Id, out var perms) ? perms : new HashSet<string>()))
            .ToList();
    }
}
