namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>A named colour label scoped to a workspace, applied to tasks via <see cref="TaskTag"/>.</summary>
public sealed class Tag : Entity, IWorkspaceOwned
{
    private Tag()
    {
    }

    private Tag(Guid id, Guid workspaceId, string name, string color)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        Color = color;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Color { get; private set; } = "#8b8b8b";

    public static Tag Create(Guid id, Guid workspaceId, string name, string? color)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new Tag(id, workspaceId, name.Trim(), string.IsNullOrWhiteSpace(color) ? "#8b8b8b" : color!);
    }
}

/// <summary>A named checklist attached to a task.</summary>
public sealed class TaskChecklist : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private readonly List<TaskChecklistItem> _items = new();

    private TaskChecklist()
    {
    }

    private TaskChecklist(Guid id, Guid taskId, string name, double position)
        : base(id)
    {
        TaskId = taskId;
        Name = name;
        Position = position;
    }

    public Guid TaskId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public double Position { get; private set; }

    public IReadOnlyList<TaskChecklistItem> Items => _items.AsReadOnly();

    public static TaskChecklist Create(Guid id, Guid taskId, string name, double position)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new TaskChecklist(id, taskId, name.Trim(), position);
    }

    public TaskChecklistItem AddItem(Guid id, string content, double position)
    {
        var item = TaskChecklistItem.Create(id, Id, content, position);
        _items.Add(item);
        return item;
    }

    /// <summary>Merge: moves this whole checklist onto another task.</summary>
    public void ReassignTask(Guid newTaskId) => TaskId = newTaskId;
}

/// <summary>A single checklist entry.</summary>
public sealed class TaskChecklistItem : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private TaskChecklistItem()
    {
    }

    private TaskChecklistItem(Guid id, Guid checklistId, string content, double position)
        : base(id)
    {
        ChecklistId = checklistId;
        Content = content;
        Position = position;
    }

    public Guid ChecklistId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public bool IsResolved { get; private set; }
    public double Position { get; private set; }

    public static TaskChecklistItem Create(Guid id, Guid checklistId, string content, double position)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(content, nameof(content));
        return new TaskChecklistItem(id, checklistId, content.Trim(), position);
    }

    public void Update(string? content, bool? isResolved, double? position)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            Content = content.Trim();
        }

        if (isResolved.HasValue)
        {
            IsResolved = isResolved.Value;
        }

        if (position.HasValue)
        {
            Position = position.Value;
        }
    }
}
