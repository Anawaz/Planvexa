namespace Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Per-resource ACL permission level (ADR-0003), ordered so numeric comparison expresses
/// privilege (Manage is highest). Distinct from the coarse <see cref="WorkspaceRole"/> — this is the
/// vocabulary used by explicit resource_permissions grants and the private-resource inheritance walk.
/// </summary>
public enum PermissionLevel
{
    View = 0,
    Comment = 1,
    Edit = 2,
    FullEdit = 3,
    Share = 4,
    Manage = 5,
}

/// <summary>A single ACL entry on a resource, projected for cross-module consumers.</summary>
public sealed record ResourcePermissionGrant(
    Guid Id,
    string ResourceType,
    Guid ResourceId,
    string PrincipalType,
    Guid PrincipalId,
    PermissionLevel Level,
    Guid GrantedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>
/// Cross-module contract (implemented by the Tenancy module, which owns tenancy.resource_permissions)
/// so other modules can resolve a caller's effective permission on one of their resources without
/// depending on Tenancy internals (AGENTS.md rule 7). Follows <see cref="IWorkspaceAccessQuery"/>'s
/// shape/DI pattern.
/// </summary>
public interface IResourcePermissionQuery
{
    /// <summary>
    /// Resolves the caller's effective permission on a resource: direct/team/role ACL grants on the
    /// resource itself, then (if the resource is not private) the same on each ancestor up the
    /// hierarchy via the registered <see cref="IResourceHierarchyQuery"/> providers, stopping hard the
    /// moment a private ancestor is reached with no matching grant. Falls back to the workspace role's
    /// coarse permission only when the walk never crossed a private boundary and found no grant at all.
    /// Returns null when the caller has no access.
    /// </summary>
    Task<PermissionLevel?> GetEffectiveAsync(
        Guid workspaceId, Guid userId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same walk as <see cref="GetEffectiveAsync(Guid,Guid,string,Guid,CancellationToken)"/>
    /// for the resource's OWN grants/private flag, but the ancestor walk continues from
    /// <paramref name="viaAncestorType"/>/<paramref name="viaAncestorId"/> instead of the resource's
    /// natural parent (as resolved by <see cref="IResourceHierarchyQuery"/>). This is how a Task's
    /// visibility is evaluated "through" one specific List membership rather than its single ambient
    /// parent — needed because a Task can now belong to several Lists (see WorkItem's doc comment), and a
    /// grant/private-flag on List B must not depend on which List the resource's hierarchy provider
    /// happens to report as the "primary" one.
    /// </summary>
    Task<PermissionLevel?> GetEffectiveViaAsync(
        Guid workspaceId, Guid userId, string resourceType, Guid resourceId,
        string viaAncestorType, Guid viaAncestorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cheap existence check (single indexed lookup) used by callers to decide whether the full,
    /// resolver-based <see cref="GetEffectiveAsync"/> is needed at all, so the common case (a
    /// non-private resource with no ACL rows) can stay on the cheap coarse-role-only path.
    /// </summary>
    Task<bool> HasAnyGrantAsync(
        Guid workspaceId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Administrative half of the ACL contract: granting/revoking resource_permissions rows. Split from
/// <see cref="IResourcePermissionQuery"/> only by concern (read vs write); both are implemented by the
/// same Tenancy-owned service. principalType/resourceType are free-form strings validated by the
/// implementation (see Tenancy's ResourcePrincipalType) rather than a shared enum, so later changes can
/// introduce new resource_type values without touching this contract or the schema.
/// </summary>
public interface IResourcePermissionAdmin
{
    Task<ResourcePermissionGrant> GrantAsync(
        Guid workspaceId, Guid actingUserId, string resourceType, Guid resourceId,
        string principalType, Guid principalId, PermissionLevel level, CancellationToken cancellationToken = default);

    Task RevokeAsync(
        Guid workspaceId, string resourceType, Guid resourceId,
        string principalType, Guid principalId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourcePermissionGrant>> ListForResourceAsync(
        Guid workspaceId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default);
}
