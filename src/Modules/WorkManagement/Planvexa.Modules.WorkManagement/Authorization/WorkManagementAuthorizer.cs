namespace Planvexa.Modules.WorkManagement.Authorization;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// WorkManagement authorization. Content operations (tasks, comments, checklists) require Member+;
/// structural operations (spaces, folders, lists, status schemes, custom fields, recurring series)
/// require Admin+. Guests are read-only. Access is resolved via <see cref="IWorkspaceAccessQuery"/>.
///
/// ADR-0003: private resources and per-resource ACL grants require the resolver-based
/// <see cref="IResourcePermissionQuery"/> check instead. The Ensure*Async overloads below are the
/// wiring point: they check the already-loaded entity's cheap <see cref="WorkEntity.IsPrivate"/> flag
/// first, then a single indexed ACL-existence check, then a bounded ancestor-privacy probe (at most 3
/// hops: Task→List→Folder→Space), and only fall through to the cheap coarse-role path when ALL of those
/// come back negative — so the common case (no privacy features used anywhere in the chain) still avoids
/// the full ACL resolver, while a private List/Folder/Space still blocks a non-private descendant reached
/// directly by id (this used to be a real gap — a private List's tasks were readable via GET /tasks/{id}
/// by any Member with no grant; closed by the ancestor probe below).
/// </summary>
public static class WorkManagementAuthorizer
{
    public static bool CanRead(WorkspaceRole? role) => role is not null;

    public static bool CanEditContent(WorkspaceRole? role) => role >= WorkspaceRole.Member;

    public static bool CanManageStructure(WorkspaceRole? role) => role >= WorkspaceRole.Admin;

    public static void EnsureRead(WorkspaceRole? role)
    {
        if (!CanRead(role))
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }
    }

    public static void EnsureEditContent(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanEditContent(role))
        {
            throw new ForbiddenException("Guests cannot modify tasks in this workspace.");
        }
    }

    public static void EnsureManageStructure(WorkspaceRole? role)
    {
        EnsureRead(role);
        if (!CanManageStructure(role))
        {
            throw new ForbiddenException("Administrator access is required to manage the workspace structure.");
        }
    }

    public static Task EnsureReadAsync(
        WorkEntity resource, WorkspaceRole? role, Guid userId, string resourceType,
        IResourcePermissionQuery acl, IResourceHierarchyQuery hierarchy, CancellationToken ct)
        => EnsureAsync(resource, role, userId, resourceType, acl, hierarchy, PermissionLevel.View, () => EnsureRead(role), ct);

    /// <summary>
    /// Same as <see cref="EnsureReadAsync"/>, but evaluates the Task's visibility "through" the
    /// specific List membership <paramref name="viaListId"/> rather than the Task's single ambient parent
    /// (which is only ever its PRIMARY list — see WorkItem's doc comment). Used by list-scoped browsing
    /// (ListByListAsync) so a Task that also belongs to a private List does not leak into a public List's
    /// view, and conversely a Task whose primary list is private is still visible when reached through a
    /// public secondary list. Direct-by-id access (GET /tasks/{id}) intentionally keeps using the plain
    /// <see cref="EnsureReadAsync"/> overload (ambient primary-list resolution, unchanged behavior).
    /// </summary>
    public static Task EnsureReadInListContextAsync(
        WorkItem task, Guid viaListId, WorkspaceRole? role, Guid userId,
        IResourcePermissionQuery acl, IResourceHierarchyQuery hierarchy, CancellationToken ct)
        => EnsureAsync(task, role, userId, WorkResourceTypes.Task, acl, hierarchy, PermissionLevel.View, () => EnsureRead(role), ct, viaListId);

    /// <summary>Non-throwing form of <see cref="EnsureReadInListContextAsync"/>.</summary>
    public static Task<bool> CanReadInListContextAsync(
        WorkItem task, Guid viaListId, WorkspaceRole? role, Guid userId,
        IResourcePermissionQuery acl, IResourceHierarchyQuery hierarchy, CancellationToken ct)
        => TryAsync(() => EnsureReadInListContextAsync(task, viaListId, role, userId, acl, hierarchy, ct));

    /// <summary>
    /// Bulk form of <see cref="CanReadInListContextAsync"/> used by WorkItemService.ListByListAsync to
    /// filter a whole list's tasks without paying one DB round trip per task. The naive per-task loop
    /// this replaces re-ran two things that are IDENTICAL for every task in the same list: the
    /// ancestor-privacy probe (depends only on <paramref name="viaListId"/>) and the workspace-role
    /// lookup (depends only on the caller) -- both are hoisted out and computed once. What's left,
    /// <see cref="IResourcePermissionQuery.HasAnyGrantAsync"/>, is batched into a single
    /// existence query via <see cref="IResourcePermissionQuery.ListResourceIdsWithGrantsAsync"/> so the
    /// common case (no private tasks, no per-task ACL grants) costs one query total instead of N, and
    /// only the (typically small) subset of tasks that are actually private or individually ACL-gated
    /// pay for the full resolver-based <see cref="IResourcePermissionQuery.GetEffectiveViaAsync"/> walk.
    /// </summary>
    public static async Task<IReadOnlyList<WorkItem>> FilterReadableInListContextAsync(
        IReadOnlyList<WorkItem> candidates, Guid viaListId, Guid workspaceId, WorkspaceRole? role, Guid userId,
        IResourcePermissionQuery acl, IResourceHierarchyQuery hierarchy, CancellationToken ct)
    {
        if (!CanRead(role) || candidates.Count == 0)
        {
            return [];
        }

        var ancestorPrivate = await AnyAncestorPrivateViaAsync(viaListId, hierarchy, ct);

        // If a private ancestor is in play, every task needs the full walk anyway (no point batching
        // the grant-existence check); otherwise, fetch the set of tasks with ANY grant in one query.
        var grantedIds = ancestorPrivate
            ? null
            : await acl.ListResourceIdsWithGrantsAsync(
                workspaceId, WorkResourceTypes.Task, candidates.Select(t => t.Id).ToList(), ct);

        var visible = new List<WorkItem>(candidates.Count);
        foreach (var task in candidates)
        {
            var needsAclCheck = ancestorPrivate || task.IsPrivate || (grantedIds?.Contains(task.Id) ?? false);
            if (!needsAclCheck)
            {
                visible.Add(task);
                continue;
            }

            var level = await acl.GetEffectiveViaAsync(
                workspaceId, userId, WorkResourceTypes.Task, task.Id, WorkResourceTypes.List, viaListId, ct);
            if (level is not null && level >= PermissionLevel.View)
            {
                visible.Add(task);
            }
        }

        return visible;
    }

    public static Task EnsureEditContentAsync(
        WorkEntity resource, WorkspaceRole? role, Guid userId, string resourceType,
        IResourcePermissionQuery acl, IResourceHierarchyQuery hierarchy, CancellationToken ct)
        => EnsureAsync(resource, role, userId, resourceType, acl, hierarchy, PermissionLevel.Edit, () => EnsureEditContent(role), ct);

    public static Task EnsureManageStructureAsync(
        WorkEntity resource, WorkspaceRole? role, Guid userId, string resourceType,
        IResourcePermissionQuery acl, IResourceHierarchyQuery hierarchy, CancellationToken ct)
        => EnsureAsync(resource, role, userId, resourceType, acl, hierarchy, PermissionLevel.Manage, () => EnsureManageStructure(role), ct);

    /// <summary>Non-throwing form used to filter a listing down to what the caller may see.</summary>
    public static Task<bool> CanReadAsync(
        WorkEntity resource, WorkspaceRole? role, Guid userId, string resourceType,
        IResourcePermissionQuery acl, IResourceHierarchyQuery hierarchy, CancellationToken ct)
        => TryAsync(() => EnsureReadAsync(resource, role, userId, resourceType, acl, hierarchy, ct));

    /// <summary>Non-throwing form used by bulk operations to skip items the caller may not edit.</summary>
    public static Task<bool> CanEditContentAsync(
        WorkEntity resource, WorkspaceRole? role, Guid userId, string resourceType,
        IResourcePermissionQuery acl, IResourceHierarchyQuery hierarchy, CancellationToken ct)
        => TryAsync(() => EnsureEditContentAsync(resource, role, userId, resourceType, acl, hierarchy, ct));

    private static async Task<bool> TryAsync(Func<Task> ensure)
    {
        try
        {
            await ensure();
            return true;
        }
        catch (ForbiddenException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks the cheap <see cref="WorkEntity.IsPrivate"/> flag, a single indexed ACL-existence check,
    /// and a bounded ancestor-privacy probe, in that order; only falls through to the cheap coarse-role
    /// path when all three come back negative.
    /// </summary>
    private static async Task EnsureAsync(
        WorkEntity resource, WorkspaceRole? role, Guid userId, string resourceType,
        IResourcePermissionQuery acl, IResourceHierarchyQuery hierarchy, PermissionLevel required, Action coarseFallback,
        CancellationToken ct, Guid? viaListId = null)
    {
        var needsAclCheck = resource.IsPrivate
            || await acl.HasAnyGrantAsync(resource.WorkspaceId, resourceType, resource.Id, ct)
            || (viaListId is { } vid
                ? await AnyAncestorPrivateViaAsync(vid, hierarchy, ct)
                : await AnyAncestorPrivateAsync(resourceType, resource.Id, hierarchy, ct));

        if (!needsAclCheck)
        {
            coarseFallback();
            return;
        }

        var level = viaListId is { } via
            ? await acl.GetEffectiveViaAsync(resource.WorkspaceId, userId, resourceType, resource.Id, WorkResourceTypes.List, via, ct)
            : await acl.GetEffectiveAsync(resource.WorkspaceId, userId, resourceType, resource.Id, ct);
        if (level is null || level < required)
        {
            throw new ForbiddenException("You do not have permission to access this resource.");
        }
    }

    /// <summary>Same probe as <see cref="AnyAncestorPrivateAsync"/>, but seeded directly at a List
    /// (the "via this specific list membership" context) instead of walking up from a Task's single
    /// ambient parent.</summary>
    private static async Task<bool> AnyAncestorPrivateViaAsync(
        Guid viaListId, IResourceHierarchyQuery hierarchy, CancellationToken ct)
    {
        string? type = WorkResourceTypes.List;
        Guid? id = viaListId;

        while (type is not null && id is not null)
        {
            var node = await hierarchy.GetAsync(type, id.Value, ct);
            if (node is null)
            {
                return false;
            }

            if (node.IsPrivate)
            {
                return true;
            }

            type = node.ParentResourceType;
            id = node.ParentResourceId;
        }

        return false;
    }

    /// <summary>
    /// Bounded ancestor-privacy probe (Task→List→Folder/Space is at most 3 hops, Space has none) — NOT
    /// the full ACL resolver, just a cheap point-read per level via the same store lookups
    /// <see cref="IResourceHierarchyQuery"/> already does for the resolver. Stops at the first private
    /// ancestor found, or when the chain runs out.
    /// </summary>
    private static async Task<bool> AnyAncestorPrivateAsync(
        string resourceType, Guid resourceId, IResourceHierarchyQuery hierarchy, CancellationToken ct)
    {
        var node = await hierarchy.GetAsync(resourceType, resourceId, ct);
        var type = node?.ParentResourceType;
        var id = node?.ParentResourceId;

        while (type is not null && id is not null)
        {
            var ancestor = await hierarchy.GetAsync(type, id.Value, ct);
            if (ancestor is null)
            {
                return false;
            }

            if (ancestor.IsPrivate)
            {
                return true;
            }

            type = ancestor.ParentResourceType;
            id = ancestor.ParentResourceId;
        }

        return false;
    }
}
