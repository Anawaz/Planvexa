namespace Planvexa.Modules.Tenancy.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>Who a <see cref="ResourcePermission"/> grant is made to.</summary>
public enum ResourcePrincipalType
{
    User = 0,
    Team = 1,
    Role = 2,
}

/// <summary>
/// ACL permission level, mirrors <see cref="SharedContracts.Workspaces.PermissionLevel"/> by ordinal
/// (see <see cref="Application.ResourcePermissionService"/> for the mapping) — kept as a distinct
/// domain-local enum rather than depending on SharedContracts from the domain layer, matching this
/// module's existing MembershipRole/WorkspaceRole duality.
/// </summary>
public enum ResourcePermissionLevel
{
    View = 0,
    Comment = 1,
    Edit = 2,
    FullEdit = 3,
    Share = 4,
    Manage = 5,
}

/// <summary>Maps <see cref="ResourcePermissionLevel"/> to/from the schema's lower_snake_case text values.</summary>
public static class ResourcePermissionLevelText
{
    public static string ToText(ResourcePermissionLevel level) => level switch
    {
        ResourcePermissionLevel.FullEdit => "full_edit",
        _ => level.ToString().ToLowerInvariant(),
    };

    public static ResourcePermissionLevel FromText(string text) => text switch
    {
        "full_edit" => ResourcePermissionLevel.FullEdit,
        _ => Enum.Parse<ResourcePermissionLevel>(text, ignoreCase: true),
    };
}

/// <summary>
/// One ACL entry (ADR-0003): grants <see cref="Level"/> access to a principal (user, team or
/// role) on one resource. <see cref="ResourceType"/> is a free-form string (e.g. "space", "folder",
/// "list", "task") so later changes can introduce new resource types without a schema change — Tenancy
/// never validates it against a fixed set, it only stores and indexes it. The resolver that interprets
/// these rows (<see cref="Application.ResourcePermissionService"/>) delegates the hierarchy walk to the
/// owning module via <see cref="SharedContracts.Workspaces.IResourceHierarchyQuery"/>.
/// </summary>
public sealed class ResourcePermission : Entity, IAggregateRoot, IWorkspaceOwned
{
    private ResourcePermission()
    {
    }

    private ResourcePermission(
        Guid id, Guid workspaceId, string resourceType, Guid resourceId,
        ResourcePrincipalType principalType, Guid principalId, ResourcePermissionLevel level,
        Guid grantedByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ResourceType = resourceType;
        ResourceId = resourceId;
        PrincipalType = principalType;
        PrincipalId = principalId;
        Level = level;
        GrantedByUserId = grantedByUserId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string ResourceType { get; private set; } = string.Empty;
    public Guid ResourceId { get; private set; }
    public ResourcePrincipalType PrincipalType { get; private set; }
    public Guid PrincipalId { get; private set; }
    public ResourcePermissionLevel Level { get; private set; }
    public Guid GrantedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ResourcePermission Create(
        Guid id, Guid workspaceId, string resourceType, Guid resourceId,
        ResourcePrincipalType principalType, Guid principalId, ResourcePermissionLevel level,
        Guid grantedByUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(workspaceId, nameof(workspaceId));
        Guard.AgainstEmpty(resourceId, nameof(resourceId));
        Guard.AgainstEmpty(grantedByUserId, nameof(grantedByUserId));
        Guard.AgainstNullOrWhiteSpace(resourceType, nameof(resourceType));

        return new ResourcePermission(
            id, workspaceId, resourceType.Trim().ToLowerInvariant(), resourceId,
            principalType, principalId, level, grantedByUserId, nowUtc);
    }

    public void SetLevel(ResourcePermissionLevel level, DateTimeOffset nowUtc)
    {
        Level = level;
        UpdatedAtUtc = nowUtc;
    }
}
