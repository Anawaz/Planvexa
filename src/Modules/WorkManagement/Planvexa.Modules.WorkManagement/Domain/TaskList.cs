namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>A List (a.k.a. Project): the direct container of tasks. Belongs to a space, optionally a folder.</summary>
public sealed class TaskList : WorkEntity, IAggregateRoot
{
    private TaskList()
    {
    }

    private TaskList(
        Guid id, Guid workspaceId, Guid spaceId, Guid? folderId, string name,
        Guid statusSchemeId, DateTimeOffset nowUtc, Guid createdBy)
        : base(id)
    {
        WorkspaceId = workspaceId;
        SpaceId = spaceId;
        FolderId = folderId;
        Name = name;
        StatusSchemeId = statusSchemeId;
        CreatedAtUtc = nowUtc;
        CreatedByUserId = createdBy;
    }

    public Guid SpaceId { get; private set; }
    public Guid? FolderId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid StatusSchemeId { get; private set; }

    /// <summary>Monotonic per-list counter powering human-friendly task sequence numbers.</summary>
    public int TaskCounter { get; private set; }

    /// <summary>The SavedView shown by default when a user opens this list (null = fall back to the first view).</summary>
    public Guid? DefaultViewId { get; private set; }

    public static TaskList Create(
        Guid id, Guid workspaceId, Guid spaceId, Guid? folderId, string name,
        Guid statusSchemeId, double position, Guid createdBy, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(spaceId, nameof(spaceId));
        Guard.AgainstEmpty(statusSchemeId, nameof(statusSchemeId));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));

        return new TaskList(id, workspaceId, spaceId, folderId, name.Trim(), statusSchemeId, nowUtc, createdBy)
        {
            Position = position,
        };
    }

    public void Update(string? name, string? description, Guid userId, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (description is not null)
        {
            Description = description;
        }

        Touch(userId, nowUtc);
    }

    /// <summary>Reserves and returns the next task sequence number for this list.</summary>
    public int NextTaskSequence() => ++TaskCounter;

    public void SetDefaultView(Guid? viewId, Guid userId, DateTimeOffset nowUtc)
    {
        DefaultViewId = viewId;
        Touch(userId, nowUtc);
    }

    /// <summary>Repoints this List at another scheme. The caller must move every task of this list onto a
    /// status of the new scheme in the same unit of work (see StatusSchemeService's remap).</summary>
    public void SetStatusScheme(Guid schemeId, Guid userId, DateTimeOffset nowUtc)
    {
        StatusSchemeId = schemeId;
        Touch(userId, nowUtc);
    }

    /// <summary>Moves this List to a different Space and/or Folder (folderId null = folderless,
    /// directly under the Space). The caller is responsible for validating both belong to this List's
    /// Workspace and that folderId (if given) actually belongs to spaceId.</summary>
    public void MoveTo(Guid spaceId, Guid? folderId, double position, Guid userId, DateTimeOffset nowUtc)
    {
        SpaceId = spaceId;
        FolderId = folderId;
        Position = position;
        Touch(userId, nowUtc);
    }
}
