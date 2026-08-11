namespace Planvexa.IntegrationTests;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Reporting.Application;
using Shouldly;
using Xunit;

internal sealed record GoalResp(Guid Id, string Name, string TargetType, decimal PercentComplete, int LinkedTaskCount, int CompletedLinkedTaskCount, int KeyResultCount);
internal sealed record GoalLinkedTaskResp(Guid TaskId, string? Title, bool? IsCompleted, bool Visible);
internal sealed record GoalKeyResultResp(Guid Id, string Title, decimal CurrentValue, decimal TargetValue, string Unit, decimal PercentComplete);
internal sealed record GoalDetailResp(GoalResp Goal, List<GoalLinkedTaskResp> LinkedTasks, List<GoalKeyResultResp> KeyResults);
internal sealed record DrillDownTaskResp(Guid TaskId, string Title, string StatusName, bool IsCompleted);
internal sealed record ScheduledReportResp(Guid Id, Guid DashboardId, List<string> Recipients, string Cadence, bool IsEnabled, DateTimeOffset? LastSentAtUtc);

/// <summary>
/// Goals/OKRs + reporting completeness. Load-bearing tests are the two permission-filtering
/// ones: a Goal's linked-task rollup/detail view and the drill-down endpoint must never reveal a task's
/// title to a viewer who could not otherwise read it (AGENTS.md rule 11 — negative/cross-permission tests
/// are mandatory for every permission-sensitive endpoint; this change's own brief calls this out as the
/// exact risk shape five earlier work already got wrong).
/// </summary>
[Collection("api")]
public sealed class GoalsAndReportingFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Goal_numeric_progress_and_linked_tasks_ratio_progress_compute_correctly()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var numeric = await CreateGoalAsync(owner, "Revenue", "Numeric", targetValue: 200m, currentValue: 50m);
        numeric.PercentComplete.ShouldBe(25m);

        var ratio = await CreateGoalAsync(owner, "Ship features", "LinkedTasksRatio");
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var t1 = await owner.CreateTaskAsync(list.Id, "Task 1");
        var t2 = await owner.CreateTaskAsync(list.Id, "Task 2");

        (await owner.PostAsJsonAsync($"/api/v1/goals/{ratio.Id}/linked-tasks", new { taskId = t1.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterOneLink = await (await owner.PostAsJsonAsync($"/api/v1/goals/{ratio.Id}/linked-tasks", new { taskId = t2.Id }))
            .Content.ReadFromJsonAsync<GoalResp>();
        afterOneLink!.LinkedTaskCount.ShouldBe(2);
        afterOneLink.PercentComplete.ShouldBe(0);

        (await owner.PostAsync(new Uri($"/api/v1/tasks/{t1.Id}/complete", UriKind.Relative), null)).EnsureSuccessStatusCode();

        var detail = await owner.GetFromJsonAsync<GoalDetailResp>($"/api/v1/goals/{ratio.Id}");
        detail!.Goal.PercentComplete.ShouldBe(50m);
    }

    [Fact]
    public async Task Key_result_CRUD_drives_the_goals_overall_progress_as_an_average()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var goal = await CreateGoalAsync(owner, "Q1 OKR", "Numeric", targetValue: 100m, currentValue: 0m);

        var kr1Resp = await owner.PostAsJsonAsync($"/api/v1/goals/{goal.Id}/key-results",
            new { title = "Revenue", targetValue = 200m, currentValue = 50m, unit = "Currency" });
        kr1Resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterKr1 = (await kr1Resp.Content.ReadFromJsonAsync<GoalResp>())!;
        afterKr1.KeyResultCount.ShouldBe(1);
        afterKr1.PercentComplete.ShouldBe(25m); // KR1 alone: 50/200 = 25%, overrides the goal's own 0/100.

        var kr2Resp = await owner.PostAsJsonAsync($"/api/v1/goals/{goal.Id}/key-results",
            new { title = "Signups", targetValue = 10m, currentValue = 10m, unit = "Number" });
        kr2Resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterKr2 = (await kr2Resp.Content.ReadFromJsonAsync<GoalResp>())!;
        afterKr2.PercentComplete.ShouldBe(62.5m); // average(25%, 100%) = 62.5%

        var detail = await owner.GetFromJsonAsync<GoalDetailResp>($"/api/v1/goals/{goal.Id}");
        detail!.KeyResults.Count.ShouldBe(2);
        var kr1 = detail.KeyResults.Single(k => k.Title == "Revenue");
        kr1.PercentComplete.ShouldBe(25m);
        kr1.Unit.ShouldBe("Currency");

        // Update KR1's current value; the rollup shifts.
        var updateResp = await owner.PutAsJsonAsync($"/api/v1/goals/{goal.Id}/key-results/{kr1.Id}", new { currentValue = 200m });
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterUpdate = (await updateResp.Content.ReadFromJsonAsync<GoalResp>())!;
        afterUpdate.PercentComplete.ShouldBe(100m); // average(100%, 100%)

        // Remove KR1; only KR2 (100%) remains.
        var removeResp = await owner.DeleteAsync(new Uri($"/api/v1/goals/{goal.Id}/key-results/{kr1.Id}", UriKind.Relative));
        removeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterRemove = (await removeResp.Content.ReadFromJsonAsync<GoalResp>())!;
        afterRemove.KeyResultCount.ShouldBe(1);
        afterRemove.PercentComplete.ShouldBe(100m);

        // Remove the last key result: falls back to the goal's own Numeric current/target (0/100 = 0%).
        var kr2 = detail.KeyResults.Single(k => k.Title == "Signups");
        var removeLast = await owner.DeleteAsync(new Uri($"/api/v1/goals/{goal.Id}/key-results/{kr2.Id}", UriKind.Relative));
        removeLast.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterRemoveLast = (await removeLast.Content.ReadFromJsonAsync<GoalResp>())!;
        afterRemoveLast.KeyResultCount.ShouldBe(0);
        afterRemoveLast.PercentComplete.ShouldBe(0m);
    }

    // ---- SECURITY: a caller from a different workspace must never see or affect another workspace's
    // goal or key results (AGENTS.md rule 11). ----
    [Fact]
    public async Task Key_result_endpoints_reject_a_caller_from_a_different_workspace()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var goal = await CreateGoalAsync(owner, "Cross-workspace OKR", "Numeric", targetValue: 100m, currentValue: 0m);
        var krResp = await owner.PostAsJsonAsync($"/api/v1/goals/{goal.Id}/key-results",
            new { title = "KR", targetValue = 10m, currentValue = 0m, unit = "Number" });
        krResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var kr = (await owner.GetFromJsonAsync<GoalDetailResp>($"/api/v1/goals/{goal.Id}"))!.KeyResults.Single();

        var (otherOwner, _, _, _) = await fixture.NewWorkspaceClientAsync();

        (await otherOwner.PostAsJsonAsync($"/api/v1/goals/{goal.Id}/key-results",
                new { title = "Intruder KR", targetValue = 5m, currentValue = 0m, unit = "Number" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await otherOwner.PutAsJsonAsync($"/api/v1/goals/{goal.Id}/key-results/{kr.Id}", new { currentValue = 999m }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await otherOwner.DeleteAsync(new Uri($"/api/v1/goals/{goal.Id}/key-results/{kr.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The key result is untouched from the owner's perspective.
        var stillThere = await owner.GetFromJsonAsync<GoalDetailResp>($"/api/v1/goals/{goal.Id}");
        stillThere!.KeyResults.Single().CurrentValue.ShouldBe(0m);
    }

    // ---- SECURITY: a Goal's linked-task detail view must permission-filter, exactly like
    // the Rollup fields and the search do. ----
    [Fact]
    public async Task Goal_linked_task_detail_hides_a_private_tasks_title_from_an_ungranted_viewer()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "goalpriv");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var visibleTask = await owner.CreateTaskAsync(list.Id, "Visible task");
        var secretTask = await owner.CreateTaskAsync(list.Id, "SECRET-TASK-TITLE");

        // Make the second task private — no grant is given to the member.
        (await owner.PatchAsJsonAsync($"/api/v1/resources/task/{secretTask.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var goal = await CreateGoalAsync(owner, "Q1 delivery", "LinkedTasksRatio");
        (await owner.PostAsJsonAsync($"/api/v1/goals/{goal.Id}/linked-tasks", new { taskId = visibleTask.Id })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/goals/{goal.Id}/linked-tasks", new { taskId = secretTask.Id })).EnsureSuccessStatusCode();

        // The owner (who has access to both) sees both titles.
        var ownerDetail = await owner.GetFromJsonAsync<GoalDetailResp>($"/api/v1/goals/{goal.Id}");
        ownerDetail!.LinkedTasks.Single(l => l.TaskId == secretTask.Id).Visible.ShouldBeTrue();
        ownerDetail.LinkedTasks.Single(l => l.TaskId == secretTask.Id).Title.ShouldBe("SECRET-TASK-TITLE");

        // The ungranted member sees the visible task's title, but the private task is masked: no title
        // leaks, Visible=false.
        var memberDetail = await memberClient.GetFromJsonAsync<GoalDetailResp>($"/api/v1/goals/{goal.Id}");
        var memberSecretEntry = memberDetail!.LinkedTasks.Single(l => l.TaskId == secretTask.Id);
        memberSecretEntry.Visible.ShouldBeFalse();
        memberSecretEntry.Title.ShouldBeNull();
        memberDetail.LinkedTasks.Single(l => l.TaskId == visibleTask.Id).Title.ShouldBe("Visible task");

        // The raw response body must never contain the secret title anywhere, not just in the mapped field.
        var raw = await memberClient.GetStringAsync($"/api/v1/goals/{goal.Id}");
        raw.ShouldNotContain("SECRET-TASK-TITLE");

        // The completion RATIO itself (an aggregate, not per-task detail) is still visible to the member —
        // only the per-task title/data is masked, per the brief's exact scope.
        memberDetail.Goal.LinkedTaskCount.ShouldBe(2);
    }

    // ---- SECURITY: drill-down from an aggregate number to its task list must permission-filter. ----
    [Fact]
    public async Task DrillDown_overdue_hides_a_private_tasks_title_from_an_ungranted_viewer()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "drilldownpriv");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var overdueTask = await owner.CreateTaskAsync(list.Id, "CONFIDENTIAL-OVERDUE-TASK");

        (await owner.PatchAsJsonAsync($"/api/v1/tasks/{overdueTask.Id}", new { dueDate = DateTimeOffset.UtcNow.AddDays(-3) }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PatchAsJsonAsync($"/api/v1/resources/task/{overdueTask.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var ownerDrillDown = await owner.GetFromJsonAsync<List<DrillDownTaskResp>>("/api/v1/reporting/drill-down/overdue");
        ownerDrillDown!.ShouldContain(t => t.TaskId == overdueTask.Id);

        var memberDrillDown = await memberClient.GetFromJsonAsync<List<DrillDownTaskResp>>("/api/v1/reporting/drill-down/overdue");
        memberDrillDown!.ShouldNotContain(t => t.TaskId == overdueTask.Id);

        var raw = await memberClient.GetStringAsync("/api/v1/reporting/drill-down/overdue");
        raw.ShouldNotContain("CONFIDENTIAL-OVERDUE-TASK");

        // Once granted View access, the member sees it.
        var members = await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members");
        var memberUserId = members!.First(m => m.Role == "Member").UserId;
        (await owner.PostAsJsonAsync($"/api/v1/resources/task/{overdueTask.Id}/permissions",
                new { principalType = "user", principalId = memberUserId, level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var afterGrant = await memberClient.GetFromJsonAsync<List<DrillDownTaskResp>>("/api/v1/reporting/drill-down/overdue");
        afterGrant!.ShouldContain(t => t.TaskId == overdueTask.Id);
    }

    // ---- The 4 widget types added alongside Burndown/Velocity/CustomFormula: TasksByAssignee,
    // TasksByPriority, CreatedVsCompleted, GoalProgress. One dashboard, one /data call, one assertion
    // per widget — end-to-end through WidgetComputer and the cross-module query contracts it composes. ----
    [Fact]
    public async Task New_widget_types_compute_tasks_by_assignee_priority_created_vs_completed_and_goal_progress()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var ownerUserId = await owner.CurrentUserIdAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);

        var t1 = await owner.CreateTaskAsync(list.Id, "Assigned urgent");
        var t2 = await owner.CreateTaskAsync(list.Id, "Unassigned high");
        var t3 = await owner.CreateTaskAsync(list.Id, "Completed for created-vs-completed");

        (await owner.PostAsJsonAsync($"/api/v1/tasks/{t1.Id}/assignees", new { userId = ownerUserId })).EnsureSuccessStatusCode();
        (await owner.PatchAsJsonAsync($"/api/v1/tasks/{t1.Id}", new { priority = "Urgent" })).EnsureSuccessStatusCode();
        (await owner.PatchAsJsonAsync($"/api/v1/tasks/{t2.Id}", new { priority = "High" })).EnsureSuccessStatusCode();
        (await owner.PostAsync(new Uri($"/api/v1/tasks/{t3.Id}/complete", UriKind.Relative), null)).EnsureSuccessStatusCode();

        var goal = await CreateGoalAsync(owner, "Ship it", "LinkedTasksRatio");
        (await owner.PostAsJsonAsync($"/api/v1/goals/{goal.Id}/linked-tasks", new { taskId = t1.Id })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/goals/{goal.Id}/linked-tasks", new { taskId = t2.Id })).EnsureSuccessStatusCode();
        (await owner.PostAsync(new Uri($"/api/v1/tasks/{t1.Id}/complete", UriKind.Relative), null)).EnsureSuccessStatusCode();

        var createDashboard = await owner.PostAsJsonAsync("/api/v1/dashboards", new
        {
            name = "New widgets",
            isPrivate = false,
            widgets = new[]
            {
                new { type = "TasksByAssignee", configJson = "{}", position = 0 },
                new { type = "TasksByPriority", configJson = "{}", position = 1 },
                new { type = "CreatedVsCompleted", configJson = "{}", position = 2 },
                new { type = "GoalProgress", configJson = "{}", position = 3 },
            },
        });
        createDashboard.EnsureSuccessStatusCode();
        var dashboard = (await createDashboard.Content.ReadFromJsonAsync<DashboardResp>())!;

        var data = (await owner.GetFromJsonAsync<List<WidgetDataResp>>($"/api/v1/dashboards/{dashboard.Id}/data"))!;

        var byAssignee = data.Single(w => w.Type == "TasksByAssignee");
        byAssignee.Series.ShouldContain(s => s.Label == ownerUserId.ToString() && s.Value == 1m);

        var byPriority = data.Single(w => w.Type == "TasksByPriority");
        byPriority.Series.ShouldContain(s => s.Label == "Urgent" && s.Value == 1m);
        byPriority.Series.ShouldContain(s => s.Label == "High" && s.Value == 1m);

        // t1, t2, t3 all created "now" (within the default 30-day range); t1 and t3 are completed.
        var createdVsCompleted = data.Single(w => w.Type == "CreatedVsCompleted");
        createdVsCompleted.Series.Single(s => s.Label == "Created").Value.ShouldBe(3m);
        createdVsCompleted.Series.Single(s => s.Label == "Completed").Value.ShouldBe(2m);

        // Ratio goal: t1 (completed) + t2 (not) linked => 50%.
        var goalProgress = data.Single(w => w.Type == "GoalProgress");
        goalProgress.Series.ShouldContain(s => s.Label == "Ship it" && s.Value == 50m);
    }

    // ---- CustomFieldBreakdown: groups tasks by a real Dropdown custom field's selected option,
    // end-to-end through CustomFieldService's own value storage + WidgetComputer +
    // WorkReportingQueries.CustomFieldValueCountsAsync (never reading WorkManagement tables directly
    // from Reporting — AGENTS.md rule 7). ----
    [Fact]
    public async Task CustomFieldBreakdown_widget_groups_tasks_by_a_dropdown_fields_selected_option()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);

        var t1 = await owner.CreateTaskAsync(list.Id, "Bug 1");
        var t2 = await owner.CreateTaskAsync(list.Id, "Bug 2");
        var t3 = await owner.CreateTaskAsync(list.Id, "Bug 3");

        var createField = await owner.PostAsJsonAsync("/api/v1/custom-fields", new
        {
            name = "Bug Severity",
            type = "Dropdown",
            scope = "Workspace",
            isRequired = false,
            options = new[] { new { label = "Critical" }, new { label = "Minor" } },
        });
        createField.StatusCode.ShouldBe(HttpStatusCode.Created);
        var field = await createField.Content.ReadFromJsonAsync<JsonElement>();
        var fieldId = field.GetProperty("id").GetGuid();
        var options = field.GetProperty("options").EnumerateArray().ToList();
        var criticalId = options.Single(o => o.GetProperty("label").GetString() == "Critical").GetProperty("id").GetGuid();
        var minorId = options.Single(o => o.GetProperty("label").GetString() == "Minor").GetProperty("id").GetGuid();

        (await owner.PutAsJsonAsync($"/api/v1/tasks/{t1.Id}/custom-fields/{fieldId}", new { value = criticalId.ToString() })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/tasks/{t2.Id}/custom-fields/{fieldId}", new { value = criticalId.ToString() })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/v1/tasks/{t3.Id}/custom-fields/{fieldId}", new { value = minorId.ToString() })).EnsureSuccessStatusCode();

        var createDashboard = await owner.PostAsJsonAsync("/api/v1/dashboards", new
        {
            name = "Custom field breakdown",
            isPrivate = false,
            widgets = new[]
            {
                new { type = "CustomFieldBreakdown", configJson = JsonSerializer.Serialize(new { customFieldId = fieldId }), position = 0 },
            },
        });
        createDashboard.EnsureSuccessStatusCode();
        var dashboard = (await createDashboard.Content.ReadFromJsonAsync<DashboardResp>())!;

        var data = (await owner.GetFromJsonAsync<List<WidgetDataResp>>($"/api/v1/dashboards/{dashboard.Id}/data"))!;
        var breakdown = data.Single(w => w.Type == "CustomFieldBreakdown");
        breakdown.Series.ShouldContain(s => s.Label == "Critical" && s.Value == 2m);
        breakdown.Series.ShouldContain(s => s.Label == "Minor" && s.Value == 1m);
    }

    [Fact]
    public async Task Scheduled_report_sends_an_export_email_when_due_and_not_before()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        await owner.CreateTaskAsync(list.Id, "For the export");

        var dashboardResp = await owner.PostAsJsonAsync("/api/v1/dashboards", new
        {
            name = "Ops dashboard",
            isPrivate = false,
            widgets = new[] { new { type = "TasksByStatus", configJson = "{}", position = 0 } },
        });
        dashboardResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dashboard = (await dashboardResp.Content.ReadFromJsonAsync<DashboardResp>())!;

        var recipient = $"report-{Guid.NewGuid():N}@planvexa.test";
        var createResp = await owner.PostAsJsonAsync("/api/v1/reporting/scheduled-reports", new
        {
            dashboardId = dashboard.Id,
            recipients = new[] { recipient },
            cadence = "Daily",
        });
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var scheduled = (await createResp.Content.ReadFromJsonAsync<ScheduledReportResp>())!;

        // Freshly created: not due yet (created "today").
        (await RunScheduledReportOnceAsync(workspaceId, scheduled.Id)).ShouldBeFalse();
        EmailLog().ForEmail(recipient).ShouldBeEmpty();

        // Backdate creation so the daily cadence is due (mirrors CollaborationPolishFlowTests' digest backdating).
        await BackdateScheduledReportAsync(scheduled.Id, TimeSpan.FromDays(2));

        (await RunScheduledReportOnceAsync(workspaceId, scheduled.Id)).ShouldBeTrue();
        var sent = EmailLog().ForEmail(recipient);
        sent.ShouldNotBeEmpty();
        sent.Last().Subject.ShouldContain(dashboard.Name);

        // Immediately re-running is a no-op (idempotent — LastSentAtUtc already advanced to "today").
        (await RunScheduledReportOnceAsync(workspaceId, scheduled.Id)).ShouldBeFalse();
        EmailLog().ForEmail(recipient).Count.ShouldBe(sent.Count);
    }

    [Fact]
    public async Task Portfolio_pdf_export_produces_a_valid_pdf_document()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync("PDF space");
        var list = await owner.CreateListAsync(space.Id);
        await owner.CreateTaskAsync(list.Id, "PDF task");

        var response = await owner.GetAsync(new Uri("/api/v1/reporting/portfolio/export.pdf", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(100);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public async Task Dashboard_xlsx_export_produces_a_valid_workbook_matching_the_data_endpoint()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync("Xlsx space");
        var list = await owner.CreateListAsync(space.Id);
        await owner.CreateTaskAsync(list.Id, "Xlsx task");

        var createDashboard = await owner.PostAsJsonAsync("/api/v1/dashboards", new
        {
            name = "Xlsx dashboard",
            isPrivate = false,
            widgets = new[] { new { type = "TasksByStatus", configJson = "{}", position = 0 } },
        });
        createDashboard.EnsureSuccessStatusCode();
        var dashboard = (await createDashboard.Content.ReadFromJsonAsync<DashboardResp>())!;

        // Same widget series the (client-side) CSV export and the xlsx export must both be built from.
        var data = (await owner.GetFromJsonAsync<List<WidgetDataResp>>($"/api/v1/dashboards/{dashboard.Id}/data"))!;
        var expectedPoint = data.Single(w => w.Type == "TasksByStatus").Series.Single();

        var response = await owner.GetAsync(new Uri($"/api/v1/dashboards/{dashboard.Id}/export.xlsx", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var xlsxBytes = await response.Content.ReadAsByteArrayAsync();
        xlsxBytes.Length.ShouldBeGreaterThan(0);

        // Round-trip check (not just "some bytes came back"): unzip the OOXML package (stdlib, no Excel
        // library needed — see XlsxWriter/FormsXlsxWriter) and confirm the worksheet XML actually contains
        // the same widget/label/value row the /data endpoint returned.
        using var zip = new ZipArchive(new MemoryStream(xlsxBytes), ZipArchiveMode.Read);
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
        sheetEntry.ShouldNotBeNull();
        using var reader = new StreamReader(sheetEntry!.Open(), Encoding.UTF8);
        var sheetXml = await reader.ReadToEndAsync();
        sheetXml.ShouldContain("TasksByStatus");
        sheetXml.ShouldContain(expectedPoint.Label);
        sheetXml.ShouldContain(expectedPoint.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<GoalResp> CreateGoalAsync(HttpClient client, string name, string targetType, decimal? targetValue = null, decimal? currentValue = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/goals", new
        {
            name,
            targetType,
            targetValue,
            currentValue,
            startDate = DateTimeOffset.UtcNow,
            endDate = DateTimeOffset.UtcNow.AddDays(90),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<GoalResp>())!;
    }

    private Planvexa.Api.Notifications.SentEmailLog EmailLog()
        => fixture.Factory.Services.GetRequiredService<Planvexa.Api.Notifications.SentEmailLog>();

    private async Task BackdateScheduledReportAsync(Guid id, TimeSpan age)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE reporting.scheduled_reports SET created_at_utc = @createdAt WHERE id = @id";
        command.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow - age);
        command.Parameters.AddWithValue("id", id);
        var affected = await command.ExecuteNonQueryAsync();
        affected.ShouldBe(1, "the scheduled report row must exist before backdating it");
    }

    /// <summary>Invokes <see cref="ScheduledReportRunner.RunAsync"/> directly under a bound workspace
    /// context, mirroring CollaborationPolishFlowTests' RunDigestOnceAsync — real daily/weekly cadences are not something a
    /// test should wait for.</summary>
    private async Task<bool> RunScheduledReportOnceAsync(Guid workspaceId, Guid reportId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWorkspaceContextAccessor>().Set(new WorkspaceContext(
            workspaceId, PlatformSystemUserId, null, string.Empty, new HashSet<string>(), new HashSet<string>(), "test-scheduled-report"));
        scope.ServiceProvider.GetRequiredService<CurrentUser>().Set(PlatformSystemUserId, "system", "system@planvexa.test", "System");

        var store = scope.ServiceProvider.GetRequiredService<IScheduledReportStore>();
        var report = await store.FindAsync(workspaceId, reportId) ?? throw new InvalidOperationException("Scheduled report not found.");

        var runner = scope.ServiceProvider.GetRequiredService<Planvexa.Modules.Reporting.Application.Services.ScheduledReportRunner>();
        return await runner.RunAsync(report, CancellationToken.None);
    }

    private static readonly Guid PlatformSystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
}
