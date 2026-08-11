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
internal sealed record SprintResp(Guid Id, string Name, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Status, int TotalPoints, string? Goal);
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
    public async Task Sprint_status_transitions_forward_only_and_are_workspace_isolated()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var createSprint = await client.PostAsJsonAsync("/api/v1/sprints", new { name = "Sprint 1", startUtc = DateTimeOffset.Parse("2026-03-01Z"), endUtc = DateTimeOffset.Parse("2026-03-14Z") });
        createSprint.EnsureSuccessStatusCode();
        var sprint = (await createSprint.Content.ReadFromJsonAsync<SprintResp>())!;
        sprint.Status.ShouldBe("Planned");

        // Invalid transition: Planned -> Completed skips Active.
        var invalid = await client.PatchAsJsonAsync($"/api/v1/sprints/{sprint.Id}/status", new { status = "Completed" });
        invalid.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Valid transition: Planned -> Active.
        var toActive = await client.PatchAsJsonAsync($"/api/v1/sprints/{sprint.Id}/status", new { status = "Active" });
        toActive.EnsureSuccessStatusCode();
        var active = (await toActive.Content.ReadFromJsonAsync<SprintResp>())!;
        active.Status.ShouldBe("Active");

        // Valid transition: Active -> Completed.
        var toCompleted = await client.PatchAsJsonAsync($"/api/v1/sprints/{sprint.Id}/status", new { status = "Completed" });
        toCompleted.EnsureSuccessStatusCode();
        var completed = (await toCompleted.Content.ReadFromJsonAsync<SprintResp>())!;
        completed.Status.ShouldBe("Completed");

        // Invalid transition: Completed -> Planned (backward) is rejected.
        var backward = await client.PatchAsJsonAsync($"/api/v1/sprints/{sprint.Id}/status", new { status = "Planned" });
        backward.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A caller from a different workspace must never see or affect this sprint.
        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var crossWorkspace = await otherClient.PatchAsJsonAsync($"/api/v1/sprints/{sprint.Id}/status", new { status = "Active" });
        crossWorkspace.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sprint_update_persists_name_dates_and_goal()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var create = await client.PostAsJsonAsync("/api/v1/sprints", new { name = "Sprint 1", startUtc = DateTimeOffset.Parse("2026-03-01Z"), endUtc = DateTimeOffset.Parse("2026-03-14Z"), goal = "Ship v1" });
        create.EnsureSuccessStatusCode();
        var sprint = (await create.Content.ReadFromJsonAsync<SprintResp>())!;
        sprint.Goal.ShouldBe("Ship v1");

        var update = await client.PatchAsJsonAsync($"/api/v1/sprints/{sprint.Id}", new
        {
            name = "Sprint 1 Renamed",
            startUtc = DateTimeOffset.Parse("2026-03-02Z"),
            endUtc = DateTimeOffset.Parse("2026-03-16Z"),
            goal = "Ship v2",
        });
        update.EnsureSuccessStatusCode();
        var updated = (await update.Content.ReadFromJsonAsync<SprintResp>())!;
        updated.Name.ShouldBe("Sprint 1 Renamed");
        updated.StartUtc.ShouldBe(DateTimeOffset.Parse("2026-03-02Z"));
        updated.EndUtc.ShouldBe(DateTimeOffset.Parse("2026-03-16Z"));
        updated.Goal.ShouldBe("Ship v2");

        // Changes persist, not just returned from the mutation response.
        var sprints = await client.GetFromJsonAsync<List<SprintResp>>("/api/v1/sprints");
        var persisted = sprints!.Single(s => s.Id == sprint.Id);
        persisted.Name.ShouldBe("Sprint 1 Renamed");
        persisted.Goal.ShouldBe("Ship v2");
    }

    [Fact]
    public async Task Sprint_delete_removes_sprint_and_its_items()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "A");

        var create = await client.PostAsJsonAsync("/api/v1/sprints", new { name = "Sprint 1", startUtc = DateTimeOffset.Parse("2026-03-01Z"), endUtc = DateTimeOffset.Parse("2026-03-14Z") });
        create.EnsureSuccessStatusCode();
        var sprint = (await create.Content.ReadFromJsonAsync<SprintResp>())!;
        (await client.PostAsJsonAsync($"/api/v1/sprints/{sprint.Id}/items", new { taskId = task.Id, points = 3 })).EnsureSuccessStatusCode();

        var del = await client.DeleteAsync(new Uri($"/api/v1/sprints/{sprint.Id}", UriKind.Relative));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var sprints = await client.GetFromJsonAsync<List<SprintResp>>("/api/v1/sprints");
        sprints!.ShouldNotContain(s => s.Id == sprint.Id);

        // The sprint's items were cascade-deleted with it -- the board (and its item lookups) no
        // longer resolve, rather than erroring on an orphaned foreign key.
        var board = await client.GetAsync(new Uri($"/api/v1/sprints/{sprint.Id}/board", UriKind.Relative));
        board.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sprint_update_and_delete_are_workspace_isolated()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/sprints", new { name = "Sprint 1", startUtc = DateTimeOffset.Parse("2026-03-01Z"), endUtc = DateTimeOffset.Parse("2026-03-14Z") });
        create.EnsureSuccessStatusCode();
        var sprint = (await create.Content.ReadFromJsonAsync<SprintResp>())!;

        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var crossUpdate = await otherClient.PatchAsJsonAsync($"/api/v1/sprints/{sprint.Id}", new { name = "Hijacked" });
        crossUpdate.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var crossDelete = await otherClient.DeleteAsync(new Uri($"/api/v1/sprints/{sprint.Id}", UriKind.Relative));
        crossDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Still intact for the owning workspace.
        var sprints = await client.GetFromJsonAsync<List<SprintResp>>("/api/v1/sprints");
        sprints!.ShouldContain(s => s.Id == sprint.Id && s.Name == "Sprint 1");
    }

    [Fact]
    public async Task Carry_over_moves_only_incomplete_items_into_the_target_sprint()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var doneA = await client.CreateTaskAsync(list.Id, "Done A");
        var doneB = await client.CreateTaskAsync(list.Id, "Done B");
        var notDone = await client.CreateTaskAsync(list.Id, "Not done");

        var createSource = await client.PostAsJsonAsync("/api/v1/sprints", new { name = "Sprint 1", startUtc = DateTimeOffset.Parse("2026-03-01Z"), endUtc = DateTimeOffset.Parse("2026-03-14Z") });
        createSource.EnsureSuccessStatusCode();
        var source = (await createSource.Content.ReadFromJsonAsync<SprintResp>())!;

        var createTarget = await client.PostAsJsonAsync("/api/v1/sprints", new { name = "Sprint 2", startUtc = DateTimeOffset.Parse("2026-03-15Z"), endUtc = DateTimeOffset.Parse("2026-03-28Z") });
        createTarget.EnsureSuccessStatusCode();
        var target = (await createTarget.Content.ReadFromJsonAsync<SprintResp>())!;

        (await client.PostAsJsonAsync($"/api/v1/sprints/{source.Id}/items", new { taskId = doneA.Id, points = 2 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/sprints/{source.Id}/items", new { taskId = doneB.Id, points = 3 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/sprints/{source.Id}/items", new { taskId = notDone.Id, points = 5 })).EnsureSuccessStatusCode();
        (await client.PostAsync(new Uri($"/api/v1/tasks/{doneA.Id}/complete", UriKind.Relative), null)).EnsureSuccessStatusCode();
        (await client.PostAsync(new Uri($"/api/v1/tasks/{doneB.Id}/complete", UriKind.Relative), null)).EnsureSuccessStatusCode();

        (await client.PatchAsJsonAsync($"/api/v1/sprints/{source.Id}/status", new { status = "Active" })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/v1/sprints/{source.Id}/status", new { status = "Completed" })).EnsureSuccessStatusCode();

        var carryOver = await client.PostAsync(new Uri($"/api/v1/sprints/{source.Id}/carry-over/{target.Id}", UriKind.Relative), null);
        carryOver.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var sourceBoard = await client.GetFromJsonAsync<BoardResp>($"/api/v1/sprints/{source.Id}/board");
        sourceBoard!.Columns.SelectMany(c => c.Tasks).Select(t => t.Id).ShouldBe(new[] { doneA.Id, doneB.Id }, ignoreOrder: true);

        var targetBoard = await client.GetFromJsonAsync<BoardResp>($"/api/v1/sprints/{target.Id}/board");
        var targetTask = targetBoard!.Columns.SelectMany(c => c.Tasks).Single();
        targetTask.Id.ShouldBe(notDone.Id);
        targetTask.Points.ShouldBe(5);
    }

    [Fact]
    public async Task Carry_over_rejects_a_target_sprint_from_another_workspace()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "A");

        var createSource = await client.PostAsJsonAsync("/api/v1/sprints", new { name = "Sprint 1", startUtc = DateTimeOffset.Parse("2026-03-01Z"), endUtc = DateTimeOffset.Parse("2026-03-14Z") });
        createSource.EnsureSuccessStatusCode();
        var source = (await createSource.Content.ReadFromJsonAsync<SprintResp>())!;
        (await client.PostAsJsonAsync($"/api/v1/sprints/{source.Id}/items", new { taskId = task.Id, points = 1 })).EnsureSuccessStatusCode();

        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var createOtherTarget = await otherClient.PostAsJsonAsync("/api/v1/sprints", new { name = "Other workspace sprint", startUtc = DateTimeOffset.Parse("2026-03-01Z"), endUtc = DateTimeOffset.Parse("2026-03-14Z") });
        createOtherTarget.EnsureSuccessStatusCode();
        var otherTarget = (await createOtherTarget.Content.ReadFromJsonAsync<SprintResp>())!;

        var crossWorkspace = await client.PostAsync(new Uri($"/api/v1/sprints/{source.Id}/carry-over/{otherTarget.Id}", UriKind.Relative), null);
        crossWorkspace.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The item is untouched -- the carry-over never partially applied.
        var sourceBoard = await client.GetFromJsonAsync<BoardResp>($"/api/v1/sprints/{source.Id}/board");
        sourceBoard!.Columns.SelectMany(c => c.Tasks).ShouldContain(t => t.Id == task.Id);
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

    // Workload's "Unassigned" bucket (Guid.Empty sentinel) surfaces open tasks nobody owns, and each
    // row -- including Unassigned -- click-throughs to its own tasks via the drill-down endpoint.
    [Fact]
    public async Task Workload_surfaces_an_unassigned_bucket_and_each_row_drills_down_to_its_own_tasks()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var ownerUserId = await OwnerUserIdAsync(client, workspaceId);

        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var assignedTask = await client.CreateTaskAsync(list.Id, "Owned task");
        var orphanTask = await client.CreateTaskAsync(list.Id, "Orphan task");

        (await client.PostAsJsonAsync($"/api/v1/tasks/{assignedTask.Id}/assignees", new { userId = ownerUserId })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/v1/tasks/{assignedTask.Id}/estimate", new { estimateSeconds = 3600 * 5 })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/v1/tasks/{orphanTask.Id}/estimate", new { estimateSeconds = 3600 * 2 })).EnsureSuccessStatusCode();

        var from = DateTimeOffset.Parse("2026-03-02T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-03-06T00:00:00Z");

        var workload = await client.GetFromJsonAsync<List<WorkloadResp>>($"/api/v1/views/workload?from={Q(from)}&to={Q(to)}");
        var unassignedRow = workload!.Single(r => r.UserId == Guid.Empty);
        unassignedRow.ScheduledHours.ShouldBe(2m);
        unassignedRow.CapacityHours.ShouldBe(0m);
        unassignedRow.IsOverAllocated.ShouldBeFalse();

        var unassignedDrillDown = await client.GetFromJsonAsync<List<DrillDownTaskResp>>($"/api/v1/reporting/drill-down/assignee/{Guid.Empty}");
        unassignedDrillDown!.Select(t => t.TaskId).ShouldBe(new[] { orphanTask.Id });

        var ownerDrillDown = await client.GetFromJsonAsync<List<DrillDownTaskResp>>($"/api/v1/reporting/drill-down/assignee/{ownerUserId}");
        ownerDrillDown!.Select(t => t.TaskId).ShouldBe(new[] { assignedTask.Id });
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

    // Workload widget: per-user scheduled hours must match each user's own assigned tasks even when
    // multiple users are assigned (regression for a bug where a shared taskIds union leaked one user's
    // estimates into another's total — see WidgetComputer.WorkloadAsync).
    [Fact]
    public async Task Workload_widget_totals_scheduled_hours_per_user_for_multiple_assignees()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var ownerUserId = await OwnerUserIdAsync(client: owner, workspaceId: workspaceId);
        var (_, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "wl");

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);

        var ownerTask1 = await owner.CreateTaskAsync(list.Id, "Owner task 1");
        var ownerTask2 = await owner.CreateTaskAsync(list.Id, "Owner task 2");
        var memberTask = await owner.CreateTaskAsync(list.Id, "Member task");

        (await owner.PostAsJsonAsync($"/api/v1/tasks/{ownerTask1.Id}/assignees", new { userId = ownerUserId })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/tasks/{ownerTask2.Id}/assignees", new { userId = ownerUserId })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/tasks/{memberTask.Id}/assignees", new { userId = memberUserId })).EnsureSuccessStatusCode();

        (await owner.PutAsJsonAsync($"/api/v1/tasks/{ownerTask1.Id}/estimate", new { estimateSeconds = 3600 * 5 })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/tasks/{ownerTask2.Id}/estimate", new { estimateSeconds = 3600 * 3 })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/tasks/{memberTask.Id}/estimate", new { estimateSeconds = 3600 * 20 })).EnsureSuccessStatusCode();

        var createDashboard = await owner.PostAsJsonAsync("/api/v1/dashboards", new
        {
            name = "Workload board",
            isPrivate = false,
            widgets = new[] { new { type = "Workload", configJson = "{}", position = 0 } },
        });
        createDashboard.EnsureSuccessStatusCode();
        var dashboard = (await createDashboard.Content.ReadFromJsonAsync<DashboardResp>())!;

        var data = await owner.GetFromJsonAsync<List<WidgetDataResp>>($"/api/v1/dashboards/{dashboard.Id}/data");
        var widget = data!.Single();
        widget.Type.ShouldBe("Workload");

        // Owner's 8h across two tasks must not be inflated by the member's 20h task, and vice versa.
        widget.Series.Single(s => s.Label == ownerUserId.ToString()).Value.ShouldBe(8m);
        widget.Series.Single(s => s.Label == memberUserId.ToString()).Value.ShouldBe(20m);
    }

    [Fact]
    public async Task Velocity_widget_computes_completed_points_for_a_finished_sprint_end_to_end()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var doneTask = await client.CreateTaskAsync(list.Id, "Done in sprint");
        var notDoneTask = await client.CreateTaskAsync(list.Id, "Not done in sprint");

        // Already ended (relative to real time) so the widget treats it as a completed sprint.
        var start = DateTimeOffset.UtcNow.AddDays(-14);
        var end = DateTimeOffset.UtcNow.AddDays(-7);
        var createSprint = await client.PostAsJsonAsync("/api/v1/sprints", new { name = "Sprint A", startUtc = start, endUtc = end });
        createSprint.EnsureSuccessStatusCode();
        var sprint = (await createSprint.Content.ReadFromJsonAsync<SprintResp>())!;

        (await client.PostAsJsonAsync($"/api/v1/sprints/{sprint.Id}/items", new { taskId = doneTask.Id, points = 5 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/sprints/{sprint.Id}/items", new { taskId = notDoneTask.Id, points = 3 })).EnsureSuccessStatusCode();
        (await client.PostAsync(new Uri($"/api/v1/tasks/{doneTask.Id}/complete", UriKind.Relative), null)).EnsureSuccessStatusCode();

        var createDashboard = await client.PostAsJsonAsync("/api/v1/dashboards", new
        {
            name = "Velocity board",
            isPrivate = false,
            widgets = new[] { new { type = "Velocity", configJson = "{}", position = 0 } },
        });
        createDashboard.EnsureSuccessStatusCode();
        var dashboard = (await createDashboard.Content.ReadFromJsonAsync<DashboardResp>())!;

        var data = await client.GetFromJsonAsync<List<WidgetDataResp>>($"/api/v1/dashboards/{dashboard.Id}/data");
        var widget = data!.Single();
        widget.Type.ShouldBe("Velocity");
        // Only the done task's 5 points count; the unfinished task's 3 points don't.
        widget.Series.ShouldContain(s => s.Label == "Sprint A" && s.Value == 5m);
        widget.Series.ShouldContain(s => s.Label == "Average" && s.Value == 5m);
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

        // Member cannot run the workspace-wide portfolio report either (Admin+, previously untested).
        var portfolioReport = await member.GetAsync(new Uri("/api/v1/reports/portfolio", UriKind.Relative));
        portfolioReport.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

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
