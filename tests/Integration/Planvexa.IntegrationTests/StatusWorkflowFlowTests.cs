namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>
/// Optional Status workflow transition restrictions (spec section 11) are enforced by the backend on
/// every status change, not only reflected in a UI that happens not to offer the disallowed option.
/// </summary>
[Collection("api")]
public sealed class StatusWorkflowFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Configuring_allowed_transitions_blocks_a_disallowed_status_change_and_still_allows_permitted_ones()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var schemes = await client.GetSchemesAsync();
        var scheme = schemes.Single(s => s.IsDefault);
        var toDo = scheme.Statuses.Single(s => s.Category == "NotStarted");
        var inProgress = scheme.Statuses.First(s => s.Category == "Active");
        var done = scheme.Statuses.Single(s => s.Category == "Done");

        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Follows the workflow");

        // Restrict "To Do" to only move to "In Progress" — jumping straight to Done is disallowed.
        var restrict = await client.PutAsJsonAsync(
            $"/api/v1/status-schemes/{scheme.Id}/statuses/{toDo.Id}/transitions", new { toStatusIds = new[] { inProgress.Id } });
        restrict.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedScheme = (await restrict.Content.ReadFromJsonAsync<SchemeResp>())!;
        updatedScheme.Statuses.Single(s => s.Id == toDo.Id).AllowedNextStatusIds.ShouldBe([inProgress.Id]);

        var jumpToDone = await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { statusId = done.Id });
        jumpToDone.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var toInProgress = await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { statusId = inProgress.Id });
        toInProgress.StatusCode.ShouldBe(HttpStatusCode.OK);

        // From In Progress (never restricted), Done is allowed.
        var thenDone = await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { statusId = done.Id });
        thenDone.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Clearing_a_transition_restriction_makes_the_status_unrestricted_again()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var scheme = (await client.GetSchemesAsync()).Single(s => s.IsDefault);
        var toDo = scheme.Statuses.Single(s => s.Category == "NotStarted");
        var inProgress = scheme.Statuses.First(s => s.Category == "Active");
        var done = scheme.Statuses.Single(s => s.Category == "Done");

        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Restriction lifted");

        (await client.PutAsJsonAsync(
                $"/api/v1/status-schemes/{scheme.Id}/statuses/{toDo.Id}/transitions", new { toStatusIds = new[] { inProgress.Id } }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PutAsJsonAsync(
                $"/api/v1/status-schemes/{scheme.Id}/statuses/{toDo.Id}/transitions", new { toStatusIds = Array.Empty<Guid>() }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var jumpToDone = await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { statusId = done.Id });
        jumpToDone.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
