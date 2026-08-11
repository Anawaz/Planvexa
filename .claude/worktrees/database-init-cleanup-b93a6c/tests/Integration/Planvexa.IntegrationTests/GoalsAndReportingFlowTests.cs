namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Reporting.Application;
using Shouldly;
using Xunit;

internal sealed record GoalResp(Guid Id, string Name, string TargetType, decimal PercentComplete, int LinkedTaskCount, int CompletedLinkedTaskCount);
internal sealed record GoalLinkedTaskResp(Guid TaskId, string? Title, bool? IsCompleted, bool Visible);
internal sealed record GoalDetailResp(GoalResp Goal, List<GoalLinkedTaskResp> LinkedTasks);
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
