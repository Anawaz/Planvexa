namespace Planvexa.Modules.Reporting.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A saved dashboard in a workspace. Owns its widgets via the aggregate. A private dashboard is only
/// visible to its owner (widget-level authorization is enforced at the service layer).
/// </summary>
public sealed class Dashboard : Entity, IAggregateRoot, IWorkspaceOwned
{
    private readonly List<DashboardWidget> _widgets = new();

    private Dashboard()
    {
    }

    private Dashboard(Guid id, Guid workspaceId, string name, bool isPrivate, Guid ownerUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        IsPrivate = isPrivate;
        OwnerUserId = ownerUserId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsPrivate { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<DashboardWidget> Widgets => _widgets.AsReadOnly();

    public static Dashboard Create(Guid id, Guid workspaceId, string name, bool isPrivate, Guid ownerUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstEmpty(ownerUserId, nameof(ownerUserId));
        return new Dashboard(id, workspaceId, name.Trim(), isPrivate, ownerUserId, nowUtc);
    }

    public DashboardWidget AddWidget(Guid id, WidgetType type, string configJson, int position)
    {
        var widget = DashboardWidget.Create(id, Id, type, configJson, position);
        _widgets.Add(widget);
        return widget;
    }

    public void Update(string? name, bool? isPrivate, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (isPrivate.HasValue)
        {
            IsPrivate = isPrivate.Value;
        }

        UpdatedAtUtc = nowUtc;
    }

    public void ReplaceWidgets(IEnumerable<(Guid Id, WidgetType Type, string ConfigJson, int Position)> widgets, DateTimeOffset nowUtc)
    {
        _widgets.Clear();
        foreach (var w in widgets)
        {
            _widgets.Add(DashboardWidget.Create(w.Id, Id, w.Type, w.ConfigJson, w.Position));
        }

        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Whether the given user may view this dashboard (owner-only when private).</summary>
    public bool CanBeViewedBy(Guid userId) => !IsPrivate || OwnerUserId == userId;

    public void EnsureViewableBy(Guid userId)
    {
        if (!CanBeViewedBy(userId))
        {
            throw new ForbiddenException("This dashboard is private to its owner.");
        }
    }
}

/// <summary>A widget on a dashboard: a widget type plus opaque JSON configuration.</summary>
public sealed class DashboardWidget : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private DashboardWidget()
    {
    }

    private DashboardWidget(Guid id, Guid dashboardId, WidgetType type, string configJson, int position)
        : base(id)
    {
        DashboardId = dashboardId;
        Type = type;
        ConfigJson = configJson;
        Position = position;
    }

    public Guid DashboardId { get; private set; }
    public WidgetType Type { get; private set; }
    public string ConfigJson { get; private set; } = "{}";
    public int Position { get; private set; }

    public static DashboardWidget Create(Guid id, Guid dashboardId, WidgetType type, string configJson, int position)
        => new(id, dashboardId, type, string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson, position);
}
