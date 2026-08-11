namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.Planning.Application;
using Planvexa.Modules.Planning.Application.Services;
using Planvexa.Modules.Reporting.Application;
using Planvexa.Modules.Reporting.Application.Services;
using Planvexa.Modules.WorkManagement.Application.Services;

// ---- Request models ----
public sealed record UpdateWorkScheduleRequest(IReadOnlyList<int> WorkingDays, decimal DailyCapacityHours);
public sealed record AddHolidayRequest(DateTimeOffset DateUtc, string Name);
public sealed record AddLeaveRequest(Guid? UserId, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string? Type);
public sealed record SetEstimateRequest(long EstimateSeconds);
public sealed record CreateSprintRequest(string Name, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
public sealed record AddSprintItemRequest(Guid TaskId, int? Points);
public sealed record WidgetInputRequest(string Type, string? ConfigJson, int Position);
public sealed record CreateDashboardRequest(string Name, bool IsPrivate, IReadOnlyList<WidgetInputRequest> Widgets);
public sealed record UpdateDashboardRequest(string? Name, bool? IsPrivate, IReadOnlyList<WidgetInputRequest>? Widgets);

/// <summary>Planning, advanced-view and dashboard endpoints.</summary>
public static class PlanningEndpoints
{
    public static void MapPlanningEndpoints(this RouteGroupBuilder api)
    {
        MapSchedule(api);
        MapHolidays(api);
        MapLeave(api);
        MapEstimates(api);
        MapSprints(api);
        MapViews(api);
        MapDashboards(api);
    }

    private static void MapSchedule(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/planning/work-schedule").RequireAuthorization();

        group.MapGet("/", async (PlanningCalendarService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetScheduleAsync(ct)));

        group.MapPut("/", async (UpdateWorkScheduleRequest r, PlanningCalendarService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateScheduleAsync(new UpdateWorkScheduleCommand(r.WorkingDays, r.DailyCapacityHours), ct)));
    }

    private static void MapHolidays(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/planning/holidays").RequireAuthorization();

        group.MapGet("/", async (PlanningCalendarService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListHolidaysAsync(ct)));

        group.MapPost("/", async (AddHolidayRequest r, PlanningCalendarService svc, CancellationToken ct) =>
        {
            var dto = await svc.AddHolidayAsync(new AddHolidayCommand(r.DateUtc, r.Name), ct);
            return Results.Created($"/api/v1/planning/holidays/{dto.Id}", dto);
        });

        group.MapDelete("/{id:guid}", async (Guid id, PlanningCalendarService svc, CancellationToken ct) =>
        {
            await svc.RemoveHolidayAsync(id, ct);
            return Results.NoContent();
        });
    }

    private static void MapLeave(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/planning/leave").RequireAuthorization();

        group.MapGet("/", async (Guid? userId, PlanningCalendarService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListLeaveAsync(userId, ct)));

        group.MapPost("/", async (AddLeaveRequest r, PlanningCalendarService svc, CancellationToken ct) =>
        {
            var dto = await svc.AddLeaveAsync(new AddLeaveCommand(r.UserId, r.StartUtc, r.EndUtc, r.Type ?? "Other"), ct);
            return Results.Created($"/api/v1/planning/leave/{dto.Id}", dto);
        });

        group.MapDelete("/{id:guid}", async (Guid id, PlanningCalendarService svc, CancellationToken ct) =>
        {
            await svc.RemoveLeaveAsync(id, ct);
            return Results.NoContent();
        });
    }

    private static void MapEstimates(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tasks/{taskId:guid}/estimate").RequireAuthorization();

        group.MapGet("/", async (Guid taskId, EstimateService svc, CancellationToken ct) =>
        {
            var dto = await svc.GetAsync(taskId, ct);
            return dto is null ? Results.Ok(new { taskId, estimateSeconds = 0L }) : Results.Ok(dto);
        });

        group.MapPut("/", async (Guid taskId, SetEstimateRequest r, EstimateService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetAsync(taskId, r.EstimateSeconds, ct)));
    }

    private static void MapSprints(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/sprints").RequireAuthorization();

        group.MapGet("/", async (SprintService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateSprintRequest r, SprintService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateAsync(new CreateSprintCommand(r.Name, r.StartUtc, r.EndUtc), ct);
            return Results.Created($"/api/v1/sprints/{dto.Id}", dto);
        });

        group.MapPost("/{id:guid}/items", async (Guid id, AddSprintItemRequest r, SprintService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddItemAsync(id, new AddSprintItemCommand(r.TaskId, r.Points), ct)));

        group.MapDelete("/{id:guid}/items/{taskId:guid}", async (Guid id, Guid taskId, SprintService svc, CancellationToken ct) =>
        {
            await svc.RemoveItemAsync(id, taskId, ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/board", async (Guid id, SprintService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetBoardAsync(id, ct)));
    }

    private static void MapViews(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/views").RequireAuthorization();

        group.MapGet("/calendar", async (DateTimeOffset from, DateTimeOffset to, Guid? scopeId, ViewQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.CalendarAsync(from, to, scopeId, ct)));

        group.MapGet("/gantt", async (Guid spaceId, ViewQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GanttAsync(spaceId, ct)));

        group.MapGet("/workload", async (DateTimeOffset from, DateTimeOffset to, WorkloadService svc, CancellationToken ct) =>
            Results.Ok(await svc.ComputeAsync(from, to, ct)));

        // Team view -- workload grouped by Team instead of flat per-individual.
        group.MapGet("/team", async (DateTimeOffset from, DateTimeOffset to, TeamWorkloadService svc, CancellationToken ct) =>
            Results.Ok(await svc.ComputeAsync(from, to, ct)));
    }

    private static void MapDashboards(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/dashboards").RequireAuthorization();

        group.MapGet("/", async (DashboardService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateDashboardRequest r, DashboardService svc, CancellationToken ct) =>
        {
            var widgets = (r.Widgets ?? Array.Empty<WidgetInputRequest>())
                .Select(w => new WidgetInput(w.Type, w.ConfigJson, w.Position)).ToList();
            var dto = await svc.CreateAsync(new CreateDashboardCommand(r.Name, r.IsPrivate, widgets), ct);
            return Results.Created($"/api/v1/dashboards/{dto.Id}", dto);
        });

        group.MapGet("/{id:guid}", async (Guid id, DashboardService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)));

        group.MapPatch("/{id:guid}", async (Guid id, UpdateDashboardRequest r, DashboardService svc, CancellationToken ct) =>
        {
            var widgets = r.Widgets?.Select(w => new WidgetInput(w.Type, w.ConfigJson, w.Position)).ToList();
            return Results.Ok(await svc.UpdateAsync(id, new UpdateDashboardCommand(r.Name, r.IsPrivate, widgets), ct));
        });

        group.MapDelete("/{id:guid}", async (Guid id, DashboardService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/data", async (Guid id, DateTimeOffset? from, DateTimeOffset? to, DashboardService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDataAsync(id, from, to, ct)));

        api.MapGet("/reports/portfolio", async (DateTimeOffset? from, DateTimeOffset? to, PortfolioService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(from, to, ct))).RequireAuthorization();
    }
}
