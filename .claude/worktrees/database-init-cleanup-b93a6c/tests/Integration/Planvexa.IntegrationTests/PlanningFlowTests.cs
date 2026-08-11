namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

// Response shapes for the planning / view / reporting endpoints.
internal sealed record WorkScheduleResp(List<int> WorkingDays, decimal DailyCapacityHours);
internal sealed record HolidayResp(Guid Id, DateTimeOffset DateUtc, string Name);
internal sealed record LeaveResp(Guid Id, Guid UserId, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Type);
internal sealed record EstimateResp(Guid TaskId, long EstimateSeconds);
internal sealed record SprintResp(Guid Id, string Name, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Status, int TotalPoints);
internal sealed record SprintItemResp(Guid TaskId, int? Points);
internal sealed record BoardCardResp(Guid Id, string Title, int? Points);
internal sealed record BoardColumnResp(Guid StatusId, string StatusName, List<BoardCardResp> Tasks);
internal sealed record BoardResp(Guid SprintId, string Name, List<BoardColumnResp> Columns);
internal sealed record WorkloadResp(Guid UserId, decimal CapacityHours, decimal ScheduledHours, decimal LoggedHours, bool IsOverAllocated);
internal sealed record CalendarResp(Guid Id, string Title, DateTimeOffset? DueDate, bool IsCompleted, string Priority);
internal sealed record GanttResp(Guid Id, string Title, DateTimeOffset? StartDate, DateTimeOffset? DueDate, bool IsMilestone, double Progress, List<Guid> DependsOn);
internal sealed record DashboardSummaryResp(Guid Id, string Name, bool IsPrivate, Guid OwnerUserId, int WidgetCount);
internal sealed record WidgetResp(Guid Id, string Type, string ConfigJson, int Position);
internal sealed record DashboardResp(Guid Id, string Name, bool IsPrivate, Guid OwnerUserId, List<WidgetResp> Widgets);
internal sealed record SeriesResp(string Label, decimal Value);
internal sealed record WidgetDataResp(Guid WidgetId, string Type, List<SeriesResp> Series);
internal sealed record PortfolioResp(string Key, string Label, int TotalTasks, int CompletedTasks, decimal LoggedHours, decimal HealthPercent);

[Collection("api")]
public sealed class PlanningFlowTests(PlanvexaFixture fixture)
{
    private static string Q(DateTimeOffset d) => Uri.EscapeDataString(d.ToString("O"));

    [Fact]
    public async Task Work_schedule_defaults_and_updates()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var initial = await client.GetFromJsonAsync<WorkScheduleResp>("/api/v1/planning/work-schedule");
        initial!.WorkingDays.ShouldBe(new[] { 1, 2, 3, 4, 5 });
        initial.DailyCapacityHours.ShouldBe(8m);

        var put = await client.PutAsJsonAsync("/api/v1/planning/work-schedule", new { workingDays = new[] { 1, 2, 3, 4 }, dailyCapacityHours = 6m });
        put.EnsureSuccessStatusCode();
        var updated = await put.Content.ReadFromJsonAsync<WorkScheduleResp>();
        updated!.WorkingDays.ShouldBe(new[] { 1, 2, 3, 4 });
        updated.DailyCapacityHours.ShouldBe(6m);
    }

    [Fact]
    public async Task Holidays_and_leave_crud()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var addHoliday = await client.PostAsJsonAsync("/api/v1/planning/holidays", new { dateUtc = DateTimeOffset.Parse("2026-12-25Z"), name = "Christmas" });
        addHoliday.EnsureSuccessStatusCode();
        var holiday = await addHoliday.Content.ReadFromJsonAsync<HolidayResp>();

        var holidays = await client.GetFromJsonAsync<List<HolidayResp>>("/api/v1/planning/holidays");
        holidays!.ShouldContain(h => h.Name == "Christmas");

        var del = await client.DeleteAsync(new Uri($"/api/v1/planning/holidays/{holiday!.Id}", UriKind.Relative));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var addLeave = await client.PostAsJsonAsync("/api/v1/planning/leave", new { startUtc = DateTimeOffset.Parse("2026-07-01Z"), endUtc = DateTimeOffset.Parse("2026-07-05Z"), type = "Vacation" });
        addLeave.EnsureSuccessStatusCode();
        var leave = (await addLeave.Content.ReadFromJsonAsync<LeaveResp>())!;
        leave.Type.ShouldBe("Vacation");

        var leaves = await client.GetFromJsonAsync<List<LeaveResp>>("/api/v1/planning/leave");
        leaves!.ShouldContain(l => l.Id == leave.Id);
    }

    [Fact]
    public async Task Estimate_can_be_set_and_read()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Estimated");

        var put = await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/estimate", new { estimateSeconds = 3600 * 8 });
        put.EnsureSuccessStatusCode();
        var estimate = await put.Content.ReadFromJsonAsync<EstimateResp>();
        estimate!.EstimateSeconds.ShouldBe(28800);

        var read = await client.GetFromJsonAsync<EstimateResp>($"/api/v1/tasks/{task.Id}/estimate");
        read!.EstimateSeconds.ShouldBe(28800);
    }

    [Fact]
    public async Task Sprint_board_groups_tasks_by_status_with_points()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var taskA = await client.CreateTaskAsync(list.Id, "A");
        var taskB = await client.CreateTaskAsync(list.Id, "B");

        var createSprint = await client.PostAsJsonAsync("/api/v1/sprints", new { name = "Sprint 1", startUtc = DateTimeOffset.Parse("2026-03-01Z"), endUtc = DateTimeOffset.Parse("2026-03-14Z") });
        createSprint.EnsureSuccessStatusCode();
        var sprint = (await createSprint.Content.ReadFromJsonAsync<SprintResp>())!;

        (await client.PostAsJsonAsync($"/api/v1/sprints/{sprint.Id}/items", new { taskId = taskA.Id, points = 3 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/sprints/{sprint.Id}/items", new { taskId = taskB.Id, points = 5 })).EnsureSuccessStatusCode();

        var sprints = await client.GetFromJsonAsync<List<SprintResp>>("/api/v1/sprints");
        sprints!.Single(s => s.Id == sprint.Id).TotalPoints.ShouldBe(8);

        var board = await client.GetFromJsonAsync<BoardResp>($"/api/v1/sprints/{sprint.Id}/board");
        board!.Columns.SelectMany(c => c.Tasks).Count().ShouldBe(2);
        board.Columns.SelectMany(c => c.Tasks).Sum(t => t.Points ?? 0).ShouldBe(8);
    }

    [Fact]
    public async Task Calendar_and_gantt_return_the_same_task_records_that_edits_change()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Dated");

        var inRange = DateTimeOffset.Parse("2026-03-10T12:00:00Z");
        (await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { startDate = DateTimeOffset.Parse("2026-03-09T09:00:00Z"), dueDate = inRange })).EnsureSuccessStatusCode();

        var from = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-03-31T00:00:00Z");

        var calendar = await client.GetFromJsonAsync<List<CalendarResp>>($"/api/v1/views/calendar?from={Q(from)}&to={Q(to)}");
        calendar!.ShouldContain(t => t.Id == task.Id);

        var gantt = await client.GetFromJsonAsync<List<GanttResp>>($"/api/v1/views/gantt?spaceId={space.Id}");
        gantt!.ShouldContain(t => t.Id == task.Id);

        // Editing the underlying task moves it out of the calendar window — proving views are projections.
        (await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { dueDate = DateTimeOffset.Parse("2026-09-10T12:00:00Z") })).EnsureSuccessStatusCode();
        var calendarAfter = await client.GetFromJsonAsync<List<CalendarResp>>($"/api/v1/views/calendar?from={Q(from)}&to={Q(to)}");
        calendarAfter!.ShouldNotContain(t => t.Id == task.Id);
    }

    [Fact]
    public async Task Workload_uses_capacity_and_estimates_and_flags_over_allocation()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var ownerUserId = await OwnerUserIdAsync(client, workspaceId);

        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Big");

        // Assign the owner and give the task a 100-hour estimate — far above a 40h weekly capacity.
        (await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/assignees", new { userId = ownerUserId })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/estimate", new { estimateSeconds = 100 * 3600 })).EnsureSuccessStatusCode();

        var from = DateTimeOffset.Parse("2026-03-02T00:00:00Z"); // Mon
        var to = DateTimeOffset.Parse("2026-03-06T00:00:00Z");   // Fri (5 working days => 40h capacity)

        var workload = await client.GetFromJsonAsync<List<WorkloadResp>>($"/api/v1/views/workload?from={Q(from)}&to={Q(to)}");
        var row = workload!.Single(r => r.UserId == ownerUserId);
        row.CapacityHours.ShouldBe(40m);
        row.ScheduledHours.ShouldBe(100m);
        row.IsOverAllocated.ShouldBeTrue();
    }

    [Fact]
    public async Task Dashboard_crud_and_widget_data_reflects_tasks()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        await client.CreateTaskAsync(list.Id, "One");
        await client.CreateTaskAsync(list.Id, "Two");

        var create = await client.PostAsJsonAsync("/api/v1/dashboards", new
        {
            name = "Ops",
            isPrivate = false,
            widgets = new[] { new { type = "TasksByStatus", configJson = "{}", position = 0 } },
        });
        create.EnsureSuccessStatusCode();
        var dashboard = (await create.Content.ReadFromJsonAsync<DashboardResp>())!;
        dashboard.Widgets.Count.ShouldBe(1);

        var list2 = await client.GetFromJsonAsync<List<DashboardSummaryResp>>("/api/v1/dashboards");
        list2!.ShouldContain(d => d.Id == dashboard.Id);

        var data = await client.GetFromJsonAsync<List<WidgetDataResp>>($"/api/v1/dashboards/{dashboard.Id}/data");
        var widget = data!.Single();
        widget.Type.ShouldBe("TasksByStatus");
        // Two tasks created, both in the default "To Do" status.
        widget.Series.Sum(s => s.Value).ShouldBe(2m);

        var del = await client.DeleteAsync(new Uri($"/api/v1/dashboards/{dashboard.Id}", UriKind.Relative));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Portfolio_report_rolls_up_tasks_by_space()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync("Delivery");
        var list = await client.CreateListAsync(space.Id);
        var t1 = await client.CreateTaskAsync(list.Id, "T1");
        await client.CreateTaskAsync(list.Id, "T2");
        (await client.PostAsync(new Uri($"/api/v1/tasks/{t1.Id}/complete", UriKind.Relative), null)).EnsureSuccessStatusCode();

        var portfolio = await client.GetFromJsonAsync<List<PortfolioResp>>("/api/v1/reports/portfolio");
        var row = portfolio!.Single(r => r.Label == "Delivery");
        row.TotalTasks.ShouldBe(2);
        row.CompletedTasks.ShouldBe(1);
        row.HealthPercent.ShouldBe(50m);
    }

    [Fact]
    public async Task Authorization_negatives_for_planning()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "pl");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        // Member cannot change the work schedule (Admin+).
        var schedule = await member.PutAsJsonAsync("/api/v1/planning/work-schedule", new { workingDays = new[] { 1, 2, 3 }, dailyCapacityHours = 5m });
        schedule.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Member cannot run the workload report (Admin+).
        var from = DateTimeOffset.Parse("2026-03-02T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-03-06T00:00:00Z");
        var workload = await member.GetAsync(new Uri($"/api/v1/views/workload?from={Q(from)}&to={Q(to)}", UriKind.Relative));
        workload.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Guest cannot create a dashboard (Member+).
        var (guestSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "gl", role: "Guest");
        var guest = fixture.WorkClient(guestSubject, slug, workspaceId);
        var dashboard = await guest.PostAsJsonAsync("/api/v1/dashboards", new { name = "x", isPrivate = false, widgets = Array.Empty<object>() });
        dashboard.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> OwnerUserIdAsync(HttpClient client, Guid workspaceId)
    {
        var members = await client.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members");
        return members!.Single(m => m.Role == "Owner").UserId;
    }
}
