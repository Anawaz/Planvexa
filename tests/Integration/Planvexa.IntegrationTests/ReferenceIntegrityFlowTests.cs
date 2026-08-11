namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>
/// A syntactically valid GUID from another Workspace must never be accepted as a User/Team/List
/// reference (AGENTS.md: never trust a caller-supplied id without validating it against the current
/// Workspace). These reproduce the gaps found in review: assignee/team-assignee/move endpoints
/// persisted any GUID with no existence-or-workspace check.
/// </summary>
[Collection("api")]
public sealed class ReferenceIntegrityFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Assigning_a_user_who_is_not_a_member_of_this_workspace_is_rejected()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var (_, workspaceBId, _, subjectB) = await fixture.NewWorkspaceClientAsync();
        var outsiderUserId = await fixture.WorkClient(subjectB, workspaceBId).CurrentUserIdAsync();

        var space = await clientA.CreateSpaceAsync();
        var list = await clientA.CreateListAsync(space.Id);
        var task = await clientA.CreateTaskAsync(list.Id, "Needs an assignee");

        var response = await clientA.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/assignees", new { userId = outsiderUserId });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await clientA.GetFromJsonAsync<TaskDetailResp2>($"/api/v1/tasks/{task.Id}"))!;
        detail.Task.AssigneeUserIds.ShouldNotContain(outsiderUserId);
    }

    [Fact]
    public async Task Creating_a_task_with_an_out_of_workspace_assignee_is_rejected()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var (_, workspaceBId, _, subjectB) = await fixture.NewWorkspaceClientAsync();
        var outsiderUserId = await fixture.WorkClient(subjectB, workspaceBId).CurrentUserIdAsync();

        var space = await clientA.CreateSpaceAsync();
        var list = await clientA.CreateListAsync(space.Id);

        var response = await clientA.PostAsJsonAsync(
            "/api/v1/tasks", new { listId = list.Id, title = "Bad assignee", assigneeUserIds = new[] { outsiderUserId } });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Assigning_a_team_from_another_workspace_is_rejected()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var (clientB, workspaceBId, _, _) = await fixture.NewWorkspaceClientAsync();

        var teamResp = await clientB.PostAsJsonAsync($"/api/v1/workspaces/{workspaceBId}/teams", new { name = "Outsiders" });
        teamResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var outsiderTeam = (await teamResp.Content.ReadFromJsonAsync<TeamResp>())!;

        var space = await clientA.CreateSpaceAsync();
        var list = await clientA.CreateListAsync(space.Id);
        var task = await clientA.CreateTaskAsync(list.Id, "Needs a team");

        var response = await clientA.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/team-assignees", new { teamId = outsiderTeam.Id });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Moving_a_task_to_a_list_in_a_different_workspace_is_rejected()
    {
        // Under ambient workspace A, a list belonging to workspace B is not merely a different
        // list to reject by id equality — it is not resolvable at all (the workspace-scoped store
        // query filter already fails it closed with 404). WorkItemService.MoveAsync additionally
        // guards task.WorkspaceId == targetList.WorkspaceId explicitly (mirroring MergeAsync's
        // existing cross-workspace guard) as defense-in-depth for any caller that resolves the
        // target list outside the ambient-filtered store.
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var spaceA = await clientA.CreateSpaceAsync();
        var listA = await clientA.CreateListAsync(spaceA.Id);
        var task = await clientA.CreateTaskAsync(listA.Id, "Stays in workspace A");

        var spaceB = await clientB.CreateSpaceAsync();
        var listB = await clientB.CreateListAsync(spaceB.Id);

        var response = await clientA.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/move", new { listId = listB.Id });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var stillInA = (await clientA.GetFromJsonAsync<TaskDetailResp2>($"/api/v1/tasks/{task.Id}"))!;
        stillInA.Task.ListId.ShouldBe(listA.Id);
    }

    [Fact]
    public async Task Setting_a_team_custom_field_to_a_team_from_another_workspace_is_rejected()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var (clientB, workspaceBId, _, _) = await fixture.NewWorkspaceClientAsync();

        var teamResp = await clientB.PostAsJsonAsync($"/api/v1/workspaces/{workspaceBId}/teams", new { name = "Outsiders" });
        var outsiderTeam = (await teamResp.Content.ReadFromJsonAsync<TeamResp>())!;

        var fieldResp = await clientA.PostAsJsonAsync(
            "/api/v1/custom-fields", new { name = "Owning team", type = "Team", scope = "Workspace", isRequired = false });
        fieldResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var field = (await fieldResp.Content.ReadFromJsonAsync<CustomFieldResp>())!;

        var space = await clientA.CreateSpaceAsync();
        var list = await clientA.CreateListAsync(space.Id);
        var task = await clientA.CreateTaskAsync(list.Id, "Needs an owning team");

        var response = await clientA.PutAsJsonAsync(
            $"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = outsiderTeam.Id.ToString() });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
