namespace Planvexa.Modules.Reporting.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Reporting.Authorization;
using Planvexa.Modules.Reporting.Domain;

/// <summary>Dashboard CRUD plus widget-data composition. Enforces private-dashboard visibility.</summary>
public sealed class DashboardService(
    ReportingServiceContext ctx,
    IDashboardStore dashboards,
    WidgetComputer widgets)
    : ReportingServiceBase(ctx)
{
    public async Task<IReadOnlyList<DashboardSummaryDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var list = await dashboards.ListByWorkspaceAsync(workspaceId, ct);
        return list
            .Where(d => d.CanBeViewedBy(UserId))
            .Select(d => new DashboardSummaryDto(d.Id, d.Name, d.IsPrivate, d.OwnerUserId, d.Widgets.Count))
            .ToList();
    }

    public async Task<DashboardDto> GetAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var dashboard = await dashboards.FindWithWidgetsAsync(id, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        dashboard.EnsureViewableBy(UserId);
        return ToDto(dashboard);
    }

    public async Task<DashboardDto> CreateAsync(CreateDashboardCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var dashboard = Dashboard.Create(NewId(), workspaceId, command.Name, command.IsPrivate, UserId, Now);
        var position = 0;
        foreach (var w in command.Widgets)
        {
            dashboard.AddWidget(NewId(), ParseType(w.Type), w.ConfigJson ?? "{}", w.Position == 0 ? position : w.Position);
            position++;
        }

        dashboards.Add(dashboard);
        Audit("reporting.dashboard.created", "Dashboard", dashboard.Id, new { dashboard.Name, dashboard.IsPrivate });
        await SaveAsync(ct);
        return ToDto(dashboard);
    }

    public async Task<DashboardDto> UpdateAsync(Guid id, UpdateDashboardCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var dashboard = await dashboards.FindWithWidgetsAsync(id, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        EnsureOwner(dashboard);

        dashboard.Update(command.Name, command.IsPrivate, Now);
        if (command.Widgets is not null)
        {
            dashboard.ReplaceWidgets(
                command.Widgets.Select((w, i) => (NewId(), ParseType(w.Type), w.ConfigJson ?? "{}", w.Position == 0 ? i : w.Position)),
                Now);
        }

        Audit("reporting.dashboard.updated", "Dashboard", dashboard.Id, new { dashboard.Name, dashboard.IsPrivate });
        await SaveAsync(ct);
        return ToDto(dashboard);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var dashboard = await dashboards.FindWithWidgetsAsync(id, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        EnsureOwner(dashboard);

        dashboards.Remove(dashboard);
        Audit("reporting.dashboard.deleted", "Dashboard", id);
        await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<WidgetDataDto>> GetDataAsync(Guid id, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var dashboard = await dashboards.FindWithWidgetsAsync(id, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        dashboard.EnsureViewableBy(UserId);

        var to = toUtc ?? Now;
        var from = fromUtc ?? to.AddDays(-30);

        var result = new List<WidgetDataDto>(dashboard.Widgets.Count);
        foreach (var widget in dashboard.Widgets.OrderBy(w => w.Position))
        {
            var series = await widgets.ComputeAsync(workspaceId, widget.Type, from, to, Now, widget.ConfigJson, ct);
            result.Add(new WidgetDataDto(widget.Id, widget.Type.ToString(), series));
        }

        return result;
    }

    /// <summary>Excel export of a dashboard's widget data — same access check and same widget series as
    /// <see cref="GetDataAsync"/> (and the row shape ScheduledReportRunner already emails as CSV), just
    /// packaged as a minimal .xlsx (see XlsxWriter).</summary>
    public async Task<(string DashboardName, byte[] Content)> ExportXlsxAsync(Guid id, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var dashboard = await dashboards.FindWithWidgetsAsync(id, ct)
            ?? throw new NotFoundException("Dashboard not found.");
        dashboard.EnsureViewableBy(UserId);

        var to = toUtc ?? Now;
        var from = fromUtc ?? to.AddDays(-30);

        var rows = new List<IReadOnlyList<string>>();
        foreach (var widget in dashboard.Widgets.OrderBy(w => w.Position))
        {
            var series = await widgets.ComputeAsync(workspaceId, widget.Type, from, to, Now, widget.ConfigJson, ct);
            foreach (var point in series)
            {
                rows.Add(new[] { widget.Type.ToString(), point.Label, point.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }
        }

        var xlsx = XlsxWriter.Write("Widgets", new[] { "Widget", "Label", "Value" }, rows);
        return (dashboard.Name, xlsx);
    }

    private void EnsureOwner(Dashboard dashboard)
    {
        if (dashboard.OwnerUserId != UserId)
        {
            throw new ForbiddenException("Only the dashboard owner can modify it.");
        }
    }

    private static WidgetType ParseType(string type)
        => Enum.TryParse<WidgetType>(type, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ValidationAppException($"Unknown widget type '{type}'.");

    private static DashboardDto ToDto(Dashboard d)
        => new(d.Id, d.Name, d.IsPrivate, d.OwnerUserId,
            d.Widgets.OrderBy(w => w.Position).Select(w => new WidgetDto(w.Id, w.Type.ToString(), w.ConfigJson, w.Position)).ToList());
}
