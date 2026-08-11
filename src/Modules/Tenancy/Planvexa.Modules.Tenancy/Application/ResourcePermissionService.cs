namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Tenancy.Authorization;
using Planvexa.Modules.Tenancy.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Implements the cross-module ACL contracts (ADR-0003): <see cref="IResourcePermissionQuery"/>
/// (the inheritance-walk resolver) and <see cref="IResourcePermissionAdmin"/> (grant/revoke). Tenancy
/// owns tenancy.resource_permissions, Team and roles; resource hierarchy/IsPrivate data belongs to
/// whichever module owns the resourceType (WorkManagement today) and is reached only through the
/// registered <see cref="IResourceHierarchyQuery"/> providers (AGENTS.md rule 7 — no direct cross-module
/// table reads).
/// </summary>
public sealed class ResourcePermissionService(
    IResourcePermissionStore acl,
    IEnumerable<IResourceHierarchyQuery> hierarchyProviders,
    IMembershipStore memberships,
    ITeamStore teams,
    IRolePermissionResolver rolePermissions,
    IIdGenerator ids,
    IClock clock,
    IAuditWriter audit) : IResourcePermissionQuery, IResourcePermissionAdmin
{
    // ---- IResourcePermissionQuery ----

    public Task<PermissionLevel?> GetEffectiveAsync(
        Guid workspaceId, Guid userId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
        => GetEffectiveCoreAsync(workspaceId, userId, resourceType, resourceId, viaAncestorType: null, viaAncestorId: null, cancellationToken);

    public Task<PermissionLevel?> GetEffectiveViaAsync(
        Guid workspaceId, Guid userId, string resourceType, Guid resourceId,
        string viaAncestorType, Guid viaAncestorId, CancellationToken cancellationToken = default)
        => GetEffectiveCoreAsync(workspaceId, userId, resourceType, resourceId, viaAncestorType, viaAncestorId, cancellationToken);

    private async Task<PermissionLevel?> GetEffectiveCoreAsync(
        Guid workspaceId, Guid userId, string resourceType, Guid resourceId,
        string? viaAncestorType, Guid? viaAncestorId, CancellationToken cancellationToken)
    {
        var member = await memberships.FindAsync(workspaceId, userId, cancellationToken);
        var teamIds = await teams.ListTeamIdsForUserAsync(workspaceId, userId, cancellationToken);

        PermissionLevel? best = null;
        var hitPrivateAncestor = false;
        string? type = resourceType;
        Guid? id = resourceId;
        var isFirstHop = true;

        // Walk the resource's own level, then its ancestors, stopping the moment a private one is
        // reached (private is a hard stop — no floor fallback beyond it, AGENTS.md-linked ADR-0003).
        while (type is not null && id is not null)
        {
            var node = await ResolveNodeAsync(type, id.Value, cancellationToken);
            if (node is null)
            {
                break;
            }

            var levelHere = BestAclLevel(
                await acl.ListForResourceAsync(workspaceId, type, id.Value, cancellationToken),
                userId, teamIds, member?.RoleId);
            if (levelHere is { } lvl && (best is null || lvl > best))
            {
                best = lvl;
            }

            if (node.IsPrivate)
            {
                // "or its owner can see it" (ADR-0003): the creator of a private resource always
                // has full control of it, even with no explicit ACL grant.
                if (node.OwnerUserId == userId)
                {
                    best = PermissionLevel.Manage;
                }

                hitPrivateAncestor = true;
                break;
            }

            // On the very first hop away from the resource itself, a caller-supplied ancestor
            // (a specific List membership) overrides the resource's natural parent as reported by its
            // hierarchy provider — every hop after that follows the natural chain from there.
            if (isFirstHop && viaAncestorType is not null && viaAncestorId is not null)
            {
                type = viaAncestorType;
                id = viaAncestorId;
            }
            else
            {
                type = node.ParentResourceType;
                id = node.ParentResourceId;
            }

            isFirstHop = false;
        }

        if (best is not null || hitPrivateAncestor)
        {
            return best;
        }

        return await FloorAsync(resourceType, member, cancellationToken);
    }

    public async Task<bool> HasAnyGrantAsync(
        Guid workspaceId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
        => await acl.AnyForResourceAsync(workspaceId, resourceType, resourceId, cancellationToken);

    public Task<IReadOnlySet<Guid>> ListResourceIdsWithGrantsAsync(
        Guid workspaceId, string resourceType, IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken = default)
        => acl.ListResourceIdsWithGrantsAsync(workspaceId, resourceType, resourceIds, cancellationToken);

    // ---- IResourcePermissionAdmin ----

    public async Task<ResourcePermissionGrant> GrantAsync(
        Guid workspaceId, Guid actingUserId, string resourceType, Guid resourceId,
        string principalType, Guid principalId, PermissionLevel level, CancellationToken cancellationToken = default)
    {
        var principal = ParsePrincipalType(principalType);
        var domainLevel = (ResourcePermissionLevel)(int)level;
        var now = clock.UtcNow;

        var existing = await acl.FindAsync(workspaceId, resourceType, resourceId, principal, principalId, cancellationToken);
        if (existing is not null)
        {
            var previousLevel = (PermissionLevel)(int)existing.Level;
            existing.SetLevel(domainLevel, now);
            audit.Write("resource_permission.updated", "ResourcePermission", existing.Id,
                new { resourceType, resourceId, principalType, principalId, previousLevel, newLevel = level });
            return ToDto(existing);
        }

        var grant = ResourcePermission.Create(
            ids.NewId(), workspaceId, resourceType, resourceId, principal, principalId, domainLevel, actingUserId, now);
        acl.Add(grant);
        audit.Write("resource_permission.granted", "ResourcePermission", grant.Id,
            new { resourceType, resourceId, principalType, principalId, level });
        return ToDto(grant);
    }

    public async Task RevokeAsync(
        Guid workspaceId, string resourceType, Guid resourceId,
        string principalType, Guid principalId, CancellationToken cancellationToken = default)
    {
        var principal = ParsePrincipalType(principalType);
        var existing = await acl.FindAsync(workspaceId, resourceType, resourceId, principal, principalId, cancellationToken)
            ?? throw new NotFoundException("Permission grant not found.");

        acl.Remove(existing);
        audit.Write("resource_permission.revoked", "ResourcePermission", existing.Id,
            new { resourceType, resourceId, principalType, principalId });
    }

    public async Task<IReadOnlyList<ResourcePermissionGrant>> ListForResourceAsync(
        Guid workspaceId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
    {
        var grants = await acl.ListForResourceAsync(workspaceId, resourceType, resourceId, cancellationToken);
        return grants.Select(ToDto).ToList();
    }

    // ---- internals ----

    private async Task<ResourceHierarchyNode?> ResolveNodeAsync(string resourceType, Guid resourceId, CancellationToken ct)
    {
        foreach (var provider in hierarchyProviders)
        {
            var node = await provider.GetAsync(resourceType, resourceId, ct);
            if (node is not null)
            {
                return node;
            }
        }

        return null;
    }

    private static PermissionLevel? BestAclLevel(
        IReadOnlyList<ResourcePermission> grants, Guid userId, IReadOnlyList<Guid> teamIds, Guid? roleId)
    {
        PermissionLevel? best = null;
        foreach (var grant in grants)
        {
            var matches = grant.PrincipalType switch
            {
                ResourcePrincipalType.User => grant.PrincipalId == userId,
                ResourcePrincipalType.Team => teamIds.Contains(grant.PrincipalId),
                ResourcePrincipalType.Role => roleId is not null && grant.PrincipalId == roleId,
                _ => false,
            };

            if (!matches)
            {
                continue;
            }

            var level = (PermissionLevel)(int)grant.Level;
            if (best is null || level > best)
            {
                best = level;
            }
        }

        return best;
    }

    /// <summary>
    /// Coarse (workspace-role) floor, consulted only when the ACL walk found nothing and never crossed
    /// a private boundary. Folders and lists share the space.* permission-key vocabulary — the original model never
    /// introduced dedicated folder.*/list.* keys, so a folder/list floor is the same as its space's.
    /// Unknown resource types (future modules) have no coarse floor here; they resolve purely from ACL
    /// grants until they define their own vocabulary.
    /// </summary>
    private async Task<PermissionLevel?> FloorAsync(string resourceType, WorkspaceMember? member, CancellationToken ct)
    {
        var permissions = await rolePermissions.ResolveAsync(member, ct);

        (string Key, PermissionLevel Level)[] candidates = resourceType switch
        {
            "space" or "folder" or "list" =>
            [
                (TenancyPermissions.SpaceManage, PermissionLevel.Manage),
                (TenancyPermissions.SpaceEdit, PermissionLevel.Edit),
                (TenancyPermissions.SpaceView, PermissionLevel.View),
            ],
            "task" =>
            [
                (TenancyPermissions.TaskManage, PermissionLevel.Manage),
                (TenancyPermissions.TaskShare, PermissionLevel.Share),
                (TenancyPermissions.TaskEdit, PermissionLevel.Edit),
                (TenancyPermissions.TaskComment, PermissionLevel.Comment),
                (TenancyPermissions.TaskView, PermissionLevel.View),
            ],
            _ => [],
        };

        PermissionLevel? best = null;
        foreach (var (key, level) in candidates)
        {
            if (permissions.Contains(key) && (best is null || level > best))
            {
                best = level;
            }
        }

        return best;
    }

    private static ResourcePrincipalType ParsePrincipalType(string principalType)
        => Enum.TryParse<ResourcePrincipalType>(principalType, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ValidationAppException($"Unknown principal type '{principalType}'. Expected user, team or role.");

    private static ResourcePermissionGrant ToDto(ResourcePermission grant) => new(
        grant.Id, grant.ResourceType, grant.ResourceId, grant.PrincipalType.ToString().ToLowerInvariant(),
        grant.PrincipalId, (PermissionLevel)(int)grant.Level, grant.GrantedByUserId, grant.CreatedAtUtc, grant.UpdatedAtUtc);
}
