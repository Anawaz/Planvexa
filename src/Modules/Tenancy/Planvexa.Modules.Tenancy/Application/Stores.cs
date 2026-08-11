namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.Modules.Tenancy.Domain;

public interface IWorkspaceStore
{
    void Add(Workspace workspace);
    Task<Workspace?> FindByIdAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);
}

public interface IMembershipStore
{
    void Add(WorkspaceMember member);
    void Remove(WorkspaceMember member);
    Task<WorkspaceMember?> FindAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);
    Task<WorkspaceMember?> FindByIdAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspaceMember>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Number of active Owners in a workspace — used to protect the last Owner.</summary>
    Task<int> CountActiveOwnersAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Every Workspace the user is an active member of, globally (bootstrap — no ambient Workspace).</summary>
    Task<IReadOnlyList<Guid>> ListWorkspaceIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IInvitationStore
{
    void Add(Invitation invitation);

    /// <summary>
    /// Looks up an invitation by token hash across all Workspaces. This intentionally bypasses the
    /// Workspace query filter because the caller is not yet a member — the token is the credential.
    /// </summary>
    Task<Invitation?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<Invitation?> FindPendingAsync(Guid workspaceId, string email, CancellationToken cancellationToken = default);

    Task<Invitation?> FindByIdAsync(Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>Pending (not yet accepted/revoked/expired) invitations for a workspace, newest first.</summary>
    Task<IReadOnlyList<Invitation>> ListPendingByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any workspace has a pending invitation for this email. Bypasses the Workspace query
    /// filter like <see cref="FindByTokenHashAsync"/> — the caller isn't a member of any workspace yet.
    /// </summary>
    Task<bool> HasPendingForEmailAsync(string email, CancellationToken cancellationToken = default);
}

public interface ITeamStore
{
    void Add(Team team);
    void Remove(Team team);
    void AddMember(TeamMembership membership);
    void RemoveMember(TeamMembership membership);
    Task<Team?> FindAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Team>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamMembership>> ListMembersAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<TeamMembership?> FindMemberAsync(Guid teamId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, int>> CountMembersByTeamAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Ids of every team the user belongs to in this workspace — used by the resource-ACL resolver.</summary>
    Task<IReadOnlyList<Guid>> ListTeamIdsForUserAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Store for the per-resource ACL (ADR-0003, tenancy.resource_permissions).</summary>
public interface IResourcePermissionStore
{
    void Add(ResourcePermission grant);
    void Remove(ResourcePermission grant);

    Task<ResourcePermission?> FindAsync(
        Guid workspaceId, string resourceType, Guid resourceId,
        ResourcePrincipalType principalType, Guid principalId, CancellationToken cancellationToken = default);

    /// <summary>Every ACL row directly on one resource (not ancestors) — the resolver aggregates across levels itself.</summary>
    Task<IReadOnlyList<ResourcePermission>> ListForResourceAsync(
        Guid workspaceId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default);

    Task<bool> AnyForResourceAsync(
        Guid workspaceId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default);

    /// <summary>Bulk form of <see cref="AnyForResourceAsync"/>: which of <paramref name="resourceIds"/> have at least one ACL row.</summary>
    Task<IReadOnlySet<Guid>> ListResourceIdsWithGrantsAsync(
        Guid workspaceId, string resourceType, IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken = default);
}

public interface IFeatureEntitlementStore
{
    void Add(FeatureEntitlement entitlement);
    void Remove(FeatureEntitlement entitlement);
    Task<IReadOnlyList<FeatureEntitlement>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

/// <summary>A role paired with its resolved permission keys — the shape the roles admin screen needs.</summary>
public sealed record RoleWithPermissions(Role Role, IReadOnlySet<string> Permissions);

public interface IRoleStore
{
    void Add(Role role);
    void AddPermission(RolePermission permission);
    Task<Role?> FindByIdAsync(Guid workspaceId, Guid roleId, CancellationToken cancellationToken = default);
    Task<Role?> FindByKeyAsync(Guid workspaceId, string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Role>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Permission keys granted to a single role. Used by <see cref="Authorization.IRolePermissionResolver"/>.</summary>
    Task<IReadOnlySet<string>> GetPermissionKeysAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Every role in a workspace with its permission keys, in one round trip (no N+1).</summary>
    Task<IReadOnlyList<RoleWithPermissions>> ListWithPermissionsAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
