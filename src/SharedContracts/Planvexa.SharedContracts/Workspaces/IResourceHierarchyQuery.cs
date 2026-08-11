namespace Planvexa.SharedContracts.Workspaces;

/// <summary>
/// One node of a resource hierarchy, as seen by the ACL inheritance-walk resolver. <c>ParentResourceType</c>/
/// <c>ParentResourceId</c> are null at the top of the hierarchy (e.g. a Space). <c>OwnerUserId</c> is the
/// resource's creator — when a node is private, its owner always has full (Manage) access even without
/// an explicit ACL grant (ADR-0003: "only ACL rows ... or its owner can see it").
/// </summary>
public sealed record ResourceHierarchyNode(
    Guid WorkspaceId, bool IsPrivate, string? ParentResourceType, Guid? ParentResourceId, Guid? OwnerUserId = null);

/// <summary>
/// Implemented by each module that owns one or more resource_type values walkable by
/// <see cref="IResourcePermissionQuery"/> (AGENTS.md rule 7 — Tenancy owns tenancy.resource_permissions
/// but not resource hierarchy/IsPrivate data, which lives on each owning module's own entities, e.g.
/// WorkManagement's Space/Folder/List/Task). All registered providers are tried in turn for a given
/// resourceType; a provider that does not own it returns null so the resolver can try the next one.
/// This is how later changes add their own resource_type strings without a schema or contract change —
/// they just register another provider.
/// </summary>
public interface IResourceHierarchyQuery
{
    Task<ResourceHierarchyNode?> GetAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default);
}
