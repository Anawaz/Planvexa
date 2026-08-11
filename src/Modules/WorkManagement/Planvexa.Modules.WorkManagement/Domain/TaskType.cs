namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A workspace-configurable task type (e.g. "Task", "Bug", "Milestone"), the same
/// shape/pattern as <see cref="StatusScheme"/>'s custom statuses — a small workspace-scoped lookup
/// table the application seeds a built-in default into lazily (see WorkspaceProvisioningService).
/// <see cref="WorkItem.TaskTypeId"/> is nullable and defaults to the built-in "Task" type.
/// </summary>
public sealed class TaskType : Entity, IWorkspaceOwned
{
    private TaskType()
    {
    }

    private TaskType(Guid id, Guid workspaceId, string name, string color, string? icon, bool isBuiltIn, double position)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        Color = color;
        Icon = icon;
        IsBuiltIn = isBuiltIn;
        Position = position;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Color { get; private set; } = "#8b8b8b";
    public string? Icon { get; private set; }

    /// <summary>Built-in types (seeded, e.g. "Task") cannot be deleted, only renamed/recolored.</summary>
    public bool IsBuiltIn { get; private set; }

    public double Position { get; private set; }

    public static TaskType Create(Guid id, Guid workspaceId, string name, string? color, string? icon, double position)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new TaskType(id, workspaceId, name.Trim(), string.IsNullOrWhiteSpace(color) ? "#8b8b8b" : color!, icon, isBuiltIn: false, position);
    }

    /// <summary>The workspace's default type, seeded lazily the first time task types are read (mirrors
    /// WorkspaceProvisioningService.EnsureDefaultSchemeAsync for StatusScheme).</summary>
    public static TaskType CreateBuiltIn(Guid id, Guid workspaceId)
        => new(id, workspaceId, "Task", "#2b7fff", "task", isBuiltIn: true, position: 0);

    public void Update(string name, string? color, string? icon)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name)).Trim();
        if (!string.IsNullOrWhiteSpace(color))
        {
            Color = color!;
        }

        Icon = icon;
    }
}
