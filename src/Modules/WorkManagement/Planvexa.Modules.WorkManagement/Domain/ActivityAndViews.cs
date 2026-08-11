namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A user-facing activity feed entry for a task (created, status changed, assigned, commented…).
/// Distinct from the security audit log: this is the human-readable task timeline.
/// </summary>
public sealed class TaskActivityEvent : Entity, IWorkspaceOwned
{
    private TaskActivityEvent()
    {
    }

    public TaskActivityEvent(
        Guid id, Guid workspaceId, Guid taskId, Guid? actorUserId,
        string type, string? data, DateTimeOffset createdAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        TaskId = taskId;
        ActorUserId = actorUserId;
        Type = type;
        Data = data;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string? Data { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}

/// <summary>
/// A saved view definition (List/Table/Board/…) scoped to a workspace/space/list. Filters, sorting,
/// grouping and column config are stored as JSON. Private views belong to <see cref="OwnerUserId"/>.
/// </summary>
public sealed class SavedView : Entity, IWorkspaceOwned
{
    private SavedView()
    {
    }

    private SavedView(
        Guid id, Guid workspaceId, CustomFieldScope scopeType, Guid? scopeId,
        string name, SavedViewType viewType, string configJson, bool isPrivate, Guid ownerUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ScopeType = scopeType;
        ScopeId = scopeId;
        Name = name;
        ViewType = viewType;
        ConfigJson = configJson;
        IsPrivate = isPrivate;
        OwnerUserId = ownerUserId;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public CustomFieldScope ScopeType { get; private set; }
    public Guid? ScopeId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public SavedViewType ViewType { get; private set; }
    public string ConfigJson { get; private set; } = "{}";
    public bool IsPrivate { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static SavedView Create(
        Guid id, Guid workspaceId, CustomFieldScope scopeType, Guid? scopeId,
        string name, SavedViewType viewType, string configJson, bool isPrivate, Guid ownerUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new SavedView(id, workspaceId, scopeType, scopeId, name.Trim(), viewType, configJson ?? "{}", isPrivate, ownerUserId, nowUtc);
    }

    public void Update(string? name, string? configJson, bool? isPrivate, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (configJson is not null)
        {
            ConfigJson = configJson;
        }

        if (isPrivate.HasValue)
        {
            IsPrivate = isPrivate.Value;
        }

        UpdatedAtUtc = nowUtc;
    }
}
