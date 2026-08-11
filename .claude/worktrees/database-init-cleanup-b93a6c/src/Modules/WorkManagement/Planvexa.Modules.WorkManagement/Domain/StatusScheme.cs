namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>A reusable set of ordered statuses that lists can adopt.</summary>
public sealed class StatusScheme : Entity, IAggregateRoot, IWorkspaceOwned
{
    private readonly List<StatusDefinition> _statuses = new();

    private StatusScheme()
    {
    }

    private StatusScheme(Guid id, Guid workspaceId, string name, bool isDefault)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        IsDefault = isDefault;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }

    public IReadOnlyList<StatusDefinition> Statuses => _statuses.AsReadOnly();

    public static StatusScheme Create(Guid id, Guid workspaceId, string name, bool isDefault)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new StatusScheme(id, workspaceId, name.Trim(), isDefault);
    }

    public StatusDefinition AddStatus(Guid id, string name, StatusCategory category, string color, double position)
    {
        var status = StatusDefinition.Create(id, Id, name, category, color, position);
        _statuses.Add(status);
        return status;
    }

    /// <summary>The status new tasks default to (first NotStarted, else first).</summary>
    public StatusDefinition DefaultStatus()
        => _statuses.OrderBy(s => s.Position).FirstOrDefault(s => s.Category == StatusCategory.NotStarted)
           ?? _statuses.OrderBy(s => s.Position).First();

    /// <summary>
    /// Builds the conventional default scheme (To Do / In Progress / Complete) for a workspace.
    /// </summary>
    public static StatusScheme CreateDefault(Guid id, Guid workspaceId, Func<Guid> newId)
    {
        var scheme = Create(id, workspaceId, "Default", isDefault: true);
        scheme.AddStatus(newId(), "To Do", StatusCategory.NotStarted, "#8b8b8b", 1024);
        scheme.AddStatus(newId(), "In Progress", StatusCategory.Active, "#2b7fff", 2048);
        scheme.AddStatus(newId(), "In Review", StatusCategory.Active, "#a855f7", 3072);
        scheme.AddStatus(newId(), "Complete", StatusCategory.Done, "#12b76a", 4096);
        return scheme;
    }
}

/// <summary>A single status within a <see cref="StatusScheme"/>.</summary>
public sealed class StatusDefinition : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private StatusDefinition()
    {
    }

    private StatusDefinition(Guid id, Guid schemeId, string name, StatusCategory category, string color, double position)
        : base(id)
    {
        SchemeId = schemeId;
        Name = name;
        Category = category;
        Color = color;
        Position = position;
    }

    public Guid SchemeId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public StatusCategory Category { get; private set; }
    public string Color { get; private set; } = "#8b8b8b";
    public double Position { get; private set; }

    public bool IsCompletedCategory => Category is StatusCategory.Done or StatusCategory.Closed;

    public static StatusDefinition Create(Guid id, Guid schemeId, string name, StatusCategory category, string color, double position)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new StatusDefinition(id, schemeId, name.Trim(), category, color, position);
    }
}
