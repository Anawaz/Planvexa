namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>
/// Permanent task delete (spec section 10.6: "Permanently delete", distinct from the recoverable
/// soft-delete every other Task test exercises) — irreversible, so only allowed from the trash, and
/// must not fail even when the task still carries child rows (assignee, checklist) that cascade.
/// </summary>
[Collection("api")]
public sealed class PermanentDeleteFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Permanently_deleting_a_task_that_is_not_in_the_trash_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Still active");

        var response = await client.DeleteAsync(new Uri($"/api/v1/tasks/{task.Id}/permanent", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Permanently_deleting_a_trashed_task_removes_it_and_its_child_rows_without_error()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Doomed");
        var (_, memberUserId) = await fixture.InviteMemberAsync(client, workspaceId, "assignee-perma");

        (await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/assignees", new { userId = memberUserId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/checklists", new { name = "Steps" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.DeleteAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var permanentResponse = await client.DeleteAsync(new Uri($"/api/v1/tasks/{task.Id}/permanent", UriKind.Relative));
        permanentResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
