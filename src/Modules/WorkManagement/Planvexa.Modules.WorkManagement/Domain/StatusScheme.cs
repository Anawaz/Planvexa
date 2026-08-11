namespace Planvexa.Modules.WorkManagement.Domain;

using System.Text.Json;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

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
    /// Configures the allowed next statuses for one status in this scheme (optional transition
    /// restriction — see spec section 11). An empty list means "unrestricted": the status may move to
    /// any other status in the scheme, which is also the default for every status until this is called.
    /// </summary>
    public void SetAllowedTransitions(Guid fromStatusId, IReadOnlyList<Guid> toStatusIds)
    {
        var from = _statuses.FirstOrDefault(s => s.Id == fromStatusId)
            ?? throw new ValidationAppException("The status does not belong to this scheme.");

        var validIds = _statuses.Select(s => s.Id).ToHashSet();
        var distinctTargets = toStatusIds.Distinct().ToList();
        if (distinctTargets.Any(id => !validIds.Contains(id)))
        {
            throw new ValidationAppException("One or more target statuses do not belong to this scheme.");
        }

        if (distinctTargets.Contains(fromStatusId))
        {
            throw new ValidationAppException("A status cannot transition to itself.");
        }

        from.SetAllowedNextStatusIds(distinctTargets);
    }

    /// <summary>
    /// True unless <paramref name="fromStatusId"/> has configured restrictions that exclude
    /// <paramref name="toStatusId"/>. A status with no configured restrictions permits any transition —
    /// restrictions are opt-in, so existing schemes keep working unchanged (spec: "Optional transition
    /// restrictions"). Unknown status ids (should not happen — callers resolve both ids against this
    /// same scheme first) are treated as unrestricted rather than throwing, since this method is a
    /// query, not a command.
    /// </summary>
    public bool CanTransition(Guid fromStatusId, Guid toStatusId)
    {
        var from = _statuses.FirstOrDefault(s => s.Id == fromStatusId);
        return from is null || from.AllowedNextStatusIds.Count == 0 || from.AllowedNextStatusIds.Contains(toStatusId);
    }

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

    /// <summary>Backing storage for <see cref="AllowedNextStatusIds"/> — null/empty means unrestricted.
    /// Only <see cref="StatusScheme.SetAllowedTransitions"/> may set this, so it can validate target ids
    /// belong to the same scheme first.</summary>
    public string? AllowedNextStatusIdsJson { get; private set; }

    public bool IsCompletedCategory => Category is StatusCategory.Done or StatusCategory.Closed;

    /// <summary>Statuses this one may transition to. Empty means unrestricted (any status in the scheme).</summary>
    public IReadOnlyList<Guid> AllowedNextStatusIds =>
        string.IsNullOrEmpty(AllowedNextStatusIdsJson)
            ? []
            : JsonSerializer.Deserialize<List<Guid>>(AllowedNextStatusIdsJson) ?? [];

    internal void SetAllowedNextStatusIds(IReadOnlyList<Guid> statusIds)
        => AllowedNextStatusIdsJson = statusIds.Count == 0 ? null : JsonSerializer.Serialize(statusIds);

    public static StatusDefinition Create(Guid id, Guid schemeId, string name, StatusCategory category, string color, double position)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new StatusDefinition(id, schemeId, name.Trim(), category, color, position);
    }
}
