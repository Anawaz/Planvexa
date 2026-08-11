namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

internal sealed record ActivityFeedItemResp(Guid Id, Guid TaskId, string TaskTitle, Guid? ActorUserId, string Type, string? Data, DateTimeOffset CreatedAtUtc);
internal sealed record TeamWorkloadMemberResp(Guid UserId, decimal CapacityHours, decimal ScheduledHours, decimal LoggedHours, bool IsOverAllocated);
internal sealed record TeamWorkloadRowResp(Guid TeamId, string TeamName, decimal CapacityHours, decimal ScheduledHours, decimal LoggedHours, List<TeamWorkloadMemberResp> Members);
internal sealed record GanttBarResp(Guid Id, string Title, DateTimeOffset? StartDate, DateTimeOffset? DueDate);
internal sealed record CalendarTaskResp(Guid Id, string Title, DateTimeOffset DueDate);

/// <summary>
/// Views completion: workspace-wide Activity feed ACL filtering (the security-sensitive item
/// here), Team view aggregation, and nested AND/OR filter-group query results.
/// </summary>
[Collection("api")]
public sealed class ViewsFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Workspace_activity_feed_hides_events_for_a_private_list_from_a_member_without_a_grant()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Engineering");

        var publicList = await ownerClient.CreateListAsync(space.Id, "Public backlog");
        var publicTask = await ownerClient.CreateTaskAsync(publicList.Id, "Public task");

        var privateList = await ownerClient.CreateListAsync(space.Id, "Confidential backlog");
        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/list/{privateList.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var privateTask = await ownerClient.CreateTaskAsync(privateList.Id, "Confidential task");

        var (memberSubject, _) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        var memberFeed = await memberClient.GetFromJsonAsync<List<ActivityFeedItemResp>>("/api/v1/activity?take=50");
        memberFeed.ShouldNotBeNull();
        memberFeed!.Select(e => e.TaskId).ShouldContain(publicTask.Id);
        memberFeed.Select(e => e.TaskId).ShouldNotContain(privateTask.Id);

        // The owner (Admin+, coarse role) still sees both -- private only removes the coarse-role floor
        // for callers without a grant (same convention as ResourcePermissionFlowTests).
        var ownerFeed = await ownerClient.GetFromJsonAsync<List<ActivityFeedItemResp>>("/api/v1/activity?take=50");
        ownerFeed.ShouldNotBeNull();
        ownerFeed!.Select(e => e.TaskId).ShouldContain(publicTask.Id);
        ownerFeed.Select(e => e.TaskId).ShouldContain(privateTask.Id);
    }

    [Fact]
    public async Task Workspace_activity_feed_grants_visibility_back_once_the_member_has_an_explicit_permission()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Engineering");
        var privateList = await ownerClient.CreateListAsync(space.Id, "Confidential backlog");
        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/list/{privateList.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var privateTask = await ownerClient.CreateTaskAsync(privateList.Id, "Confidential task");

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        (await memberClient.GetFromJsonAsync<List<ActivityFeedItemResp>>("/api/v1/activity?take=50"))!
            .Select(e => e.TaskId).ShouldNotContain(privateTask.Id);

        var grant = await ownerClient.PostAsJsonAsync(
            $"/api/v1/resources/list/{privateList.Id}/permissions",
            new { principalType = "user", principalId = memberUserId, level = "view" });
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await memberClient.GetFromJsonAsync<List<ActivityFeedItemResp>>("/api/v1/activity?take=50"))!
            .Select(e => e.TaskId).ShouldContain(privateTask.Id);
    }

    /// <summary>
    /// Regression: ViewQueryService.GanttAsync used to return every task in the space with only a
    /// coarse workspace-role check (IWorkReportingQueries does no ACL filtering). Fixed to re-filter
    /// per task through the same CanReadAsync check as ListByListAsync/the Activity feed. GET
    /// /api/v1/views/gantt is also the exact endpoint the new Timeline view consumes, so this
    /// test covers Timeline's leak too -- there is no separate Timeline endpoint to test.
    /// </summary>
    [Fact]
    public async Task Gantt_view_hides_a_private_lists_task_from_a_member_without_a_grant()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Engineering");

        var publicList = await ownerClient.CreateListAsync(space.Id, "Public backlog");
        var publicTask = await ownerClient.CreateTaskAsync(publicList.Id, "Public task");

        var privateList = await ownerClient.CreateListAsync(space.Id, "Confidential backlog");
        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/list/{privateList.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var privateTask = await ownerClient.CreateTaskAsync(privateList.Id, "Confidential task");

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        var memberBars = await memberClient.GetFromJsonAsync<List<GanttBarResp>>($"/api/v1/views/gantt?spaceId={space.Id}");
        memberBars.ShouldNotBeNull();
        memberBars!.Select(b => b.Id).ShouldContain(publicTask.Id);
        memberBars.Select(b => b.Id).ShouldNotContain(privateTask.Id);

        // The owner (Admin+, coarse role) still sees both.
        var ownerBars = await ownerClient.GetFromJsonAsync<List<GanttBarResp>>($"/api/v1/views/gantt?spaceId={space.Id}");
        ownerBars.ShouldNotBeNull();
        ownerBars!.Select(b => b.Id).ShouldContain(publicTask.Id);
        ownerBars.Select(b => b.Id).ShouldContain(privateTask.Id);

        // An explicit grant restores visibility for the Member, same as the Activity feed.
        var grant = await ownerClient.PostAsJsonAsync(
            $"/api/v1/resources/list/{privateList.Id}/permissions",
            new { principalType = "user", principalId = memberUserId, level = "view" });
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        var memberBarsAfterGrant = await memberClient.GetFromJsonAsync<List<GanttBarResp>>($"/api/v1/views/gantt?spaceId={space.Id}");
        memberBarsAfterGrant!.Select(b => b.Id).ShouldContain(privateTask.Id);
    }

    /// <summary>Same leak, same fix, for ViewQueryService.CalendarAsync (GET /api/v1/views/calendar).</summary>
    [Fact]
    public async Task Calendar_view_hides_a_private_lists_task_from_a_member_without_a_grant()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Engineering");
        var dueDate = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(3), TimeSpan.Zero);

        var publicList = await ownerClient.CreateListAsync(space.Id, "Public backlog");
        var publicTask = await ownerClient.CreateTaskAsync(publicList.Id, "Public task");
        (await ownerClient.PatchAsJsonAsync($"/api/v1/tasks/{publicTask.Id}", new { dueDate })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var privateList = await ownerClient.CreateListAsync(space.Id, "Confidential backlog");
        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/list/{privateList.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var privateTask = await ownerClient.CreateTaskAsync(privateList.Id, "Confidential task");
        (await ownerClient.PatchAsJsonAsync($"/api/v1/tasks/{privateTask.Id}", new { dueDate })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var (memberSubject, _) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        var from = Uri.EscapeDataString(dueDate.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(dueDate.AddDays(1).ToString("O"));
        var memberTasks = await memberClient.GetFromJsonAsync<List<CalendarTaskResp>>($"/api/v1/views/calendar?from={from}&to={to}");
        memberTasks.ShouldNotBeNull();
        memberTasks!.Select(t => t.Id).ShouldContain(publicTask.Id);
        memberTasks.Select(t => t.Id).ShouldNotContain(privateTask.Id);

        var ownerTasks = await ownerClient.GetFromJsonAsync<List<CalendarTaskResp>>($"/api/v1/views/calendar?from={from}&to={to}");
        ownerTasks.ShouldNotBeNull();
        ownerTasks!.Select(t => t.Id).ShouldContain(publicTask.Id);
        ownerTasks.Select(t => t.Id).ShouldContain(privateTask.Id);
    }

    [Fact]
    public async Task Team_view_groups_workload_by_team_membership()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var (_, memberUserId) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "teammate");

        var team = await ReadAsync<TeamResp>(await ownerClient.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/teams", new { name = "Platform", description = (string?)null }));
        (await ownerClient.PostAsJsonAsync($"/api/v1/teams/{team.Id}/members", new { userId = memberUserId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(7).ToString("O"));
        var rows = await ReadAsync<List<TeamWorkloadRowResp>>(await ownerClient.GetAsync(new Uri($"/api/v1/views/team?from={from}&to={to}", UriKind.Relative)));

        rows.ShouldNotBeNull();
        var platformRow = rows!.Single(r => r.TeamId == team.Id);
        platformRow.TeamName.ShouldBe("Platform");
        platformRow.Members.Single(m => m.UserId == memberUserId);
    }

    [Fact]
    public async Task Nested_filter_groups_narrow_a_list_query_to_the_matching_tasks()
    {
        var (ownerClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Engineering");
        var list = await ownerClient.CreateListAsync(space.Id, "Backlog");

        var urgentTask = await ownerClient.CreateTaskAsync(list.Id, "Fix outage");
        (await ownerClient.PatchAsJsonAsync($"/api/v1/tasks/{urgentTask.Id}", new { priority = "Urgent" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var normalTask = await ownerClient.CreateTaskAsync(list.Id, "Update docs");
        (await ownerClient.PatchAsJsonAsync($"/api/v1/tasks/{normalTask.Id}", new { priority = "Low" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // priority = Urgent OR title contains "docs"
        var filter = new
        {
            logic = "Or",
            conditions = new[]
            {
                new { field = "priority", @operator = "Equals", value = "Urgent" },
                new { field = "title", @operator = "Contains", value = "docs" },
            },
        };

        var response = await ownerClient.PostAsJsonAsync($"/api/v1/lists/{list.Id}/tasks/query", filter);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var matched = (await response.Content.ReadFromJsonAsync<List<TaskResp>>())!;

        matched.Select(t => t.Id).ShouldContain(urgentTask.Id);
        matched.Select(t => t.Id).ShouldContain(normalTask.Id);
        matched.Count.ShouldBe(2);

        // priority = Urgent AND title contains "docs" -- matches neither.
        var narrowFilter = new
        {
            logic = "And",
            conditions = new[]
            {
                new { field = "priority", @operator = "Equals", value = "Urgent" },
                new { field = "title", @operator = "Contains", value = "docs" },
            },
        };

        var narrowResponse = await ownerClient.PostAsJsonAsync($"/api/v1/lists/{list.Id}/tasks/query", narrowFilter);
        var narrowMatched = (await narrowResponse.Content.ReadFromJsonAsync<List<TaskResp>>())!;
        narrowMatched.ShouldBeEmpty();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{(int)response.StatusCode} from {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: "
                + await response.Content.ReadAsStringAsync());
        }

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
