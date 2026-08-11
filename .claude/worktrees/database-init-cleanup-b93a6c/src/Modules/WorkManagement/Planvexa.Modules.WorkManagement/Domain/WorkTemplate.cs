namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>The resource kind a <see cref="WorkTemplate"/> was captured from / can be applied as.</summary>
public enum TemplateResourceType
{
    Space = 0,
    Folder = 1,
    List = 2,
}

/// <summary>
/// A reusable structural snapshot of a Space/Folder/List — sub-structure (folders/lists), status
/// scheme reference and custom-field definitions, but never task instances/content — that a later
/// "create from template" operation can replay to pre-populate a new resource. The snapshot is stored as
/// opaque JSON (<see cref="StructureJson"/>); WorkTemplateService owns its shape (see
/// TemplateStructure/TemplateSnapshotBuilder), matching how SavedView.ConfigJson is opaque to the domain
/// layer.
/// </summary>
public sealed class WorkTemplate : Entity, IWorkspaceOwned
{
    private WorkTemplate()
    {
    }

    private WorkTemplate(
        Guid id, Guid workspaceId, TemplateResourceType resourceType, string name, string structureJson,
        Guid createdByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ResourceType = resourceType;
        Name = name;
        StructureJson = structureJson;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public TemplateResourceType ResourceType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string StructureJson { get; private set; } = "{}";
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static WorkTemplate Create(
        Guid id, Guid workspaceId, TemplateResourceType resourceType, string name, string structureJson,
        Guid createdByUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new WorkTemplate(id, workspaceId, resourceType, name.Trim(), structureJson, createdByUserId, nowUtc);
    }
}

/// <summary>
/// A user's favourite/bookmark of a work resource (Space/Folder/List, or generically any other
/// WorkManagement resource type — see WorkResourceTypes). Free-form <see cref="ResourceType"/> mirrors
/// tenancy.resource_permissions' free-form resource_type so later resource kinds need no schema change.
/// </summary>
public sealed class WorkFavorite : Entity, IWorkspaceOwned
{
    private WorkFavorite()
    {
    }

    private WorkFavorite(Guid id, Guid workspaceId, Guid userId, string resourceType, Guid resourceId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        ResourceType = resourceType;
        ResourceId = resourceId;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public string ResourceType { get; private set; } = string.Empty;
    public Guid ResourceId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static WorkFavorite Create(Guid id, Guid workspaceId, Guid userId, string resourceType, Guid resourceId, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(resourceId, nameof(resourceId));
        Guard.AgainstNullOrWhiteSpace(resourceType, nameof(resourceType));
        return new WorkFavorite(id, workspaceId, userId, resourceType, resourceId, nowUtc);
    }
}

/// <summary>
/// P8: a user's most-recent view of a resource, across any resource kind — free-form <see cref="ResourceType"/>
/// mirrors <see cref="WorkFavorite"/>'s convention so later resource kinds need no schema change. One row
/// per (workspace, user, resourceType, resourceId): repeat views bump <see cref="ViewedAtUtc"/> via
/// <see cref="Touch"/> rather than inserting a duplicate; RecentItemService caps the row count per user by
/// deleting the oldest overflow after an insert.
/// </summary>
public sealed class RecentItem : Entity, IWorkspaceOwned
{
    private RecentItem()
    {
    }

    private RecentItem(Guid id, Guid workspaceId, Guid userId, string resourceType, Guid resourceId, DateTimeOffset viewedAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        ResourceType = resourceType;
        ResourceId = resourceId;
        ViewedAtUtc = viewedAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public string ResourceType { get; private set; } = string.Empty;
    public Guid ResourceId { get; private set; }
    public DateTimeOffset ViewedAtUtc { get; private set; }

    public static RecentItem Create(Guid id, Guid workspaceId, Guid userId, string resourceType, Guid resourceId, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(resourceId, nameof(resourceId));
        Guard.AgainstNullOrWhiteSpace(resourceType, nameof(resourceType));
        return new RecentItem(id, workspaceId, userId, resourceType, resourceId, nowUtc);
    }

    public void Touch(DateTimeOffset nowUtc) => ViewedAtUtc = nowUtc;
}
