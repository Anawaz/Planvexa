namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>
/// Managing statuses (add/rename/recolour/reorder/remove) and the Space-level override of the workspace
/// default scheme. The rule these tests exist to pin down: nothing may strand a task on a status that is
/// going away — the caller always names the replacement, and the tasks are actually moved there.
/// </summary>
[Collection("api")]
public sealed class StatusSchemeManagementTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Adding_updating_and_moving_a_status_round_trips()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var scheme = (await client.GetSchemesAsync()).Single(s => s.IsDefault);

        var added = await ReadSchemeAsync(await client.PostAsJsonAsync(
            $"/api/v1/status-schemes/{scheme.Id}/statuses", new { name = "Blocked", category = "active", color = "#ff0000" }));
        var blocked = added.Statuses.Single(s => s.Name == "Blocked");
        blocked.Category.ShouldBe("Active");
        blocked.Color.ShouldBe("#ff0000");
        added.Statuses.Last().Id.ShouldBe(blocked.Id, "a new status is appended");

        var updated = await ReadSchemeAsync(await client.PatchAsJsonAsync(
            $"/api/v1/status-schemes/{scheme.Id}/statuses/{blocked.Id}",
            new { name = "On Hold", category = "NotStarted", color = "#123456" }));
        var onHold = updated.Statuses.Single(s => s.Id == blocked.Id);
        onHold.Name.ShouldBe("On Hold");
        onHold.Category.ShouldBe("NotStarted");
        onHold.Color.ShouldBe("#123456");

        var moved = await ReadSchemeAsync(await client.PatchAsJsonAsync(
            $"/api/v1/status-schemes/{scheme.Id}/statuses/{blocked.Id}", new { index = 0 }));
        moved.Statuses[0].Id.ShouldBe(blocked.Id);

        var renamed = await ReadSchemeAsync(await client.PatchAsJsonAsync(
            $"/api/v1/status-schemes/{scheme.Id}", new { name = "House Workflow" }));
        renamed.Name.ShouldBe("House Workflow");
    }

    [Fact]
    public async Task Removing_a_status_moves_its_tasks_to_the_replacement_and_keeps_the_completion_flag_consistent()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var scheme = (await client.GetSchemesAsync()).Single(s => s.IsDefault);
        var inReview = scheme.Statuses.Single(s => s.Name == "In Review");
        var done = scheme.Statuses.Single(s => s.Category == "Done");

        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Sitting on In Review");
        (await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { statusId = inReview.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await ReadSchemeAsync(await client.DeleteAsJsonAsync(
            $"/api/v1/status-schemes/{scheme.Id}/statuses/{inReview.Id}", new { moveTasksToStatusId = done.Id }));
        after.Statuses.ShouldNotContain(s => s.Id == inReview.Id);

        // The target is a Done-category status, so the task must come out completed — the remap goes
        // through WorkItem.ChangeStatus, not a raw UPDATE, precisely so this stays true.
        var moved = await GetTaskAsync(client, task.Id);
        moved.StatusId.ShouldBe(done.Id);
        moved.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Removing_a_status_moved_onto_an_open_status_clears_the_completion_flag()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var scheme = (await client.GetSchemesAsync()).Single(s => s.IsDefault);
        var toDo = scheme.Statuses.Single(s => s.Category == "NotStarted");
        var done = scheme.Statuses.Single(s => s.Category == "Done");

        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Completed, then its status is removed");
        (await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { statusId = done.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.DeleteAsJsonAsync(
                $"/api/v1/status-schemes/{scheme.Id}/statuses/{done.Id}", new { moveTasksToStatusId = toDo.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var moved = await GetTaskAsync(client, task.Id);
        moved.StatusId.ShouldBe(toDo.Id);
        moved.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Removing_a_status_rejects_a_missing_self_referential_or_foreign_replacement()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var scheme = (await client.GetSchemesAsync()).Single(s => s.IsDefault);
        var inReview = scheme.Statuses.Single(s => s.Name == "In Review");

        var other = await CreateSchemeAsync(client, "Other", ("Open", "NotStarted"), ("Shut", "Done"));

        (await client.DeleteAsJsonAsync(
                $"/api/v1/status-schemes/{scheme.Id}/statuses/{inReview.Id}", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.DeleteAsJsonAsync(
                $"/api/v1/status-schemes/{scheme.Id}/statuses/{inReview.Id}", new { moveTasksToStatusId = inReview.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.DeleteAsJsonAsync(
                $"/api/v1/status-schemes/{scheme.Id}/statuses/{inReview.Id}", new { moveTasksToStatusId = other.Statuses[0].Id }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Removing_the_only_status_of_a_scheme_conflicts()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var lonely = await CreateSchemeAsync(client, "Single", ("Only", "NotStarted"));
        var elsewhere = (await client.GetSchemesAsync()).Single(s => s.IsDefault).Statuses[0];

        (await client.DeleteAsJsonAsync(
                $"/api/v1/status-schemes/{lonely.Id}/statuses/{lonely.Statuses[0].Id}",
                new { moveTasksToStatusId = elsewhere.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Deleting_the_default_scheme_or_one_still_used_by_a_list_conflicts()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var defaultScheme = (await client.GetSchemesAsync()).Single(s => s.IsDefault);

        (await client.DeleteAsync($"/api/v1/status-schemes/{defaultScheme.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var spare = await CreateSchemeAsync(client, "Spare", ("Open", "NotStarted"), ("Shut", "Done"));
        var space = await client.CreateSpaceAsync();
        var response = await client.PostAsJsonAsync(
            "/api/v1/lists", new { spaceId = space.Id, name = "Uses spare", statusSchemeId = spare.Id });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var inUse = await client.DeleteAsync($"/api/v1/status-schemes/{spare.Id}");
        inUse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await inUse.Content.ReadAsStringAsync()).ShouldContain("1 list uses this workflow.");
    }

    [Fact]
    public async Task Deleting_an_unused_scheme_succeeds()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spare = await CreateSchemeAsync(client, "Unused", ("Open", "NotStarted"));

        (await client.DeleteAsync($"/api/v1/status-schemes/{spare.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetSchemesAsync()).ShouldNotContain(s => s.Id == spare.Id);
    }

    [Fact]
    public async Task Removing_a_status_scrubs_it_from_the_other_statuses_allowed_transitions()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var scheme = (await client.GetSchemesAsync()).Single(s => s.IsDefault);
        var toDo = scheme.Statuses.Single(s => s.Category == "NotStarted");
        var inReview = scheme.Statuses.Single(s => s.Name == "In Review");
        var done = scheme.Statuses.Single(s => s.Category == "Done");

        (await client.PutAsJsonAsync(
                $"/api/v1/status-schemes/{scheme.Id}/statuses/{toDo.Id}/transitions",
                new { toStatusIds = new[] { inReview.Id, done.Id } }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await ReadSchemeAsync(await client.DeleteAsJsonAsync(
            $"/api/v1/status-schemes/{scheme.Id}/statuses/{inReview.Id}", new { moveTasksToStatusId = done.Id }));

        // A dangling id here would make CanTransition silently mis-evaluate for every later change.
        after.Statuses.Single(s => s.Id == toDo.Id).AllowedNextStatusIds.ShouldBe([done.Id]);
    }

    [Fact]
    public async Task Customizing_a_space_clones_the_scheme_and_leaves_the_other_space_and_the_workspace_default_alone()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var workspaceDefault = (await client.GetSchemesAsync()).Single(s => s.IsDefault);

        var spaceA = await client.CreateSpaceAsync("Alpha");
        var spaceB = await client.CreateSpaceAsync("Beta");
        var listA = await client.CreateListAsync(spaceA.Id, "A list");
        var listB = await client.CreateListAsync(spaceB.Id, "B list");
        var taskA = await client.CreateTaskAsync(listA.Id, "Stays put");

        var inherited = (await client.GetFromJsonAsync<SpaceSchemeResp>($"/api/v1/spaces/{spaceA.Id}/status-scheme"))!;
        inherited.IsCustomized.ShouldBeFalse();
        inherited.Scheme.Id.ShouldBe(workspaceDefault.Id);

        var customized = (await (await client.PostAsJsonAsync(
            $"/api/v1/spaces/{spaceA.Id}/status-scheme", new { })).Content.ReadFromJsonAsync<SpaceSchemeResp>())!;
        customized.IsCustomized.ShouldBeTrue();
        customized.Scheme.Id.ShouldNotBe(workspaceDefault.Id);
        customized.Scheme.SpaceId.ShouldBe(spaceA.Id);
        customized.Scheme.IsDefault.ShouldBeFalse();
        customized.Scheme.Statuses.Select(s => s.Name)
            .ShouldBe(workspaceDefault.Statuses.Select(s => s.Name).ToList());

        // Only Space A moved: Space B's list and the workspace default scheme are untouched.
        (await GetListAsync(client, listA.Id)).StatusSchemeId.ShouldBe(customized.Scheme.Id);
        (await GetListAsync(client, listB.Id)).StatusSchemeId.ShouldBe(workspaceDefault.Id);

        // Space A's task followed the clone's matching status, not the workspace default's.
        var movedTask = await GetTaskAsync(client, taskA.Id);
        customized.Scheme.Statuses.ShouldContain(s => s.Id == movedTask.StatusId);
        workspaceDefault.Statuses.ShouldNotContain(s => s.Id == movedTask.StatusId);

        // Editing the Space's scheme afterwards must not leak into the workspace default.
        (await client.PostAsJsonAsync(
                $"/api/v1/status-schemes/{customized.Scheme.Id}/statuses", new { name = "Space Only", category = "Active" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var defaultAfter = (await client.GetSchemesAsync()).Single(s => s.Id == workspaceDefault.Id);
        defaultAfter.IsDefault.ShouldBeTrue();
        defaultAfter.Statuses.Count.ShouldBe(workspaceDefault.Statuses.Count);
        defaultAfter.Statuses.ShouldNotContain(s => s.Name == "Space Only");

        // The workspace-level listing keeps the Space override out of the settings page.
        var workspaceLevel = (await client.GetFromJsonAsync<List<SchemeResp>>(
            "/api/v1/status-schemes?workspaceLevelOnly=true"))!;
        workspaceLevel.ShouldNotContain(s => s.Id == customized.Scheme.Id);
        workspaceLevel.ShouldContain(s => s.Id == workspaceDefault.Id);
    }

    [Fact]
    public async Task Customizing_a_space_twice_returns_the_same_override()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();

        var first = (await (await client.PostAsJsonAsync(
            $"/api/v1/spaces/{space.Id}/status-scheme", new { })).Content.ReadFromJsonAsync<SpaceSchemeResp>())!;
        var second = (await (await client.PostAsJsonAsync(
            $"/api/v1/spaces/{space.Id}/status-scheme", new { })).Content.ReadFromJsonAsync<SpaceSchemeResp>())!;

        second.Scheme.Id.ShouldBe(first.Scheme.Id);
        second.Scheme.Statuses.Count.ShouldBe(first.Scheme.Statuses.Count);
    }

    [Fact]
    public async Task A_new_list_in_a_customized_space_picks_up_the_space_scheme()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();

        var customized = (await (await client.PostAsJsonAsync(
            $"/api/v1/spaces/{space.Id}/status-scheme",
            new { presetStatuses = new[] { new { name = "Backlog", category = "NotStarted", color = (string?)null }, new { name = "Shipped", category = "Done", color = (string?)null } } }))
            .Content.ReadFromJsonAsync<SpaceSchemeResp>())!;
        customized.Scheme.Statuses.Select(s => s.Name).ShouldBe(["Backlog", "Shipped"]);

        var list = await client.CreateListAsync(space.Id, "Created after customizing");
        list.StatusSchemeId.ShouldBe(customized.Scheme.Id);

        var task = await client.CreateTaskAsync(list.Id, "Uses the space workflow");
        customized.Scheme.Statuses.ShouldContain(s => s.Id == task.StatusId);
    }

    [Fact]
    public async Task Customizing_a_space_leaves_a_task_that_is_only_cross_listed_into_it_alone()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var workspaceDefault = (await client.GetSchemesAsync()).Single(s => s.IsDefault);

        var spaceA = await client.CreateSpaceAsync("Alpha");
        var spaceB = await client.CreateSpaceAsync("Beta");
        var listA = await client.CreateListAsync(spaceA.Id, "A list");
        var listB = await client.CreateListAsync(spaceB.Id, "B list");

        // Primary list is in Space A; the task is only ADDED to Space B's list.
        var task = await client.CreateTaskAsync(listA.Id, "Lives in A, shown in B");
        (await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/lists", new { listId = listB.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var before = (await GetTaskAsync(client, task.Id)).StatusId;

        var customized = (await (await client.PostAsJsonAsync(
            $"/api/v1/spaces/{spaceB.Id}/status-scheme", new { })).Content.ReadFromJsonAsync<SpaceSchemeResp>())!;

        // Its status still belongs to Space A's (unchanged, workspace-default) workflow — moving it onto
        // Space B's clone would make it foreign to its own primary list's scheme.
        var after = await GetTaskAsync(client, task.Id);
        after.StatusId.ShouldBe(before);
        workspaceDefault.Statuses.ShouldContain(s => s.Id == after.StatusId);
        customized.Scheme.Statuses.ShouldNotContain(s => s.Id == after.StatusId);
        (await GetListAsync(client, listA.Id)).StatusSchemeId.ShouldBe(workspaceDefault.Id);
    }

    [Fact]
    public async Task Reverting_a_space_rejects_a_mapping_from_a_status_outside_the_space_scheme()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var workspaceDefault = (await client.GetSchemesAsync()).Single(s => s.IsDefault);
        var defaultToDo = workspaceDefault.Statuses.Single(s => s.Category == "NotStarted");
        var defaultDone = workspaceDefault.Statuses.Single(s => s.Category == "Done");

        var other = await client.CreateSpaceAsync("Untouched");
        var otherList = await client.CreateListAsync(other.Id, "Untouched list");
        var bystander = await client.CreateTaskAsync(otherList.Id, "Must not move");

        var space = await client.CreateSpaceAsync("Customized");
        var list = await client.CreateListAsync(space.Id);
        await client.CreateTaskAsync(list.Id, "In the customized space");
        (await client.PostAsJsonAsync($"/api/v1/spaces/{space.Id}/status-scheme", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // fromStatusId belongs to the workspace default scheme, not this Space's override.
        var rejected = await client.DeleteAsJsonAsync(
            $"/api/v1/spaces/{space.Id}/status-scheme",
            new { mapping = new[] { new { fromStatusId = defaultToDo.Id, toStatusId = defaultDone.Id } } });
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await GetTaskAsync(client, bystander.Id)).StatusId.ShouldBe(defaultToDo.Id);
    }

    [Fact]
    public async Task Reverting_a_space_remaps_tasks_per_the_mapping_and_deletes_the_space_scheme()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var workspaceDefault = (await client.GetSchemesAsync()).Single(s => s.IsDefault);
        var defaultDone = workspaceDefault.Statuses.Single(s => s.Category == "Done");

        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Rides the revert");

        var customized = (await (await client.PostAsJsonAsync(
            $"/api/v1/spaces/{space.Id}/status-scheme", new { })).Content.ReadFromJsonAsync<SpaceSchemeResp>())!;
        var current = (await GetTaskAsync(client, task.Id)).StatusId;

        // Without a mapping for the status the task sits on, the revert is refused rather than guessing.
        (await client.DeleteAsJsonAsync($"/api/v1/spaces/{space.Id}/status-scheme", new { mapping = Array.Empty<object>() }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var reverted = await ReadAsync<SpaceSchemeResp>(await client.DeleteAsJsonAsync(
            $"/api/v1/spaces/{space.Id}/status-scheme",
            new { mapping = new[] { new { fromStatusId = current, toStatusId = defaultDone.Id } } }));

        reverted.IsCustomized.ShouldBeFalse();
        reverted.Scheme.Id.ShouldBe(workspaceDefault.Id);

        (await GetListAsync(client, list.Id)).StatusSchemeId.ShouldBe(workspaceDefault.Id);

        var moved = await GetTaskAsync(client, task.Id);
        moved.StatusId.ShouldBe(defaultDone.Id);
        moved.IsCompleted.ShouldBeTrue();

        (await client.GetSchemesAsync()).ShouldNotContain(s => s.Id == customized.Scheme.Id);
        (await client.GetFromJsonAsync<SpaceSchemeResp>($"/api/v1/spaces/{space.Id}/status-scheme"))!
            .IsCustomized.ShouldBeFalse();
    }

    [Fact]
    public async Task A_caller_from_another_workspace_gets_404()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var (outsider, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var scheme = (await owner.GetSchemesAsync()).Single(s => s.IsDefault);
        var space = await owner.CreateSpaceAsync();

        (await outsider.PatchAsJsonAsync($"/api/v1/status-schemes/{scheme.Id}", new { name = "Hijacked" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await outsider.PostAsJsonAsync(
                $"/api/v1/status-schemes/{scheme.Id}/statuses", new { name = "Sneaky", category = "Active" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await outsider.DeleteAsJsonAsync(
                $"/api/v1/status-schemes/{scheme.Id}/statuses/{scheme.Statuses[0].Id}",
                new { moveTasksToStatusId = scheme.Statuses[1].Id }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await outsider.DeleteAsync($"/api/v1/status-schemes/{scheme.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await outsider.GetAsync($"/api/v1/spaces/{space.Id}/status-scheme"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await outsider.PostAsJsonAsync($"/api/v1/spaces/{space.Id}/status-scheme", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>GET /tasks/{id} returns a TaskDetailDto envelope, not the task itself.</summary>
    private static async Task<TaskResp> GetTaskAsync(HttpClient client, Guid taskId)
        => (await client.GetFromJsonAsync<TaskEnvelope>($"/api/v1/tasks/{taskId}"))!.Task;

    private sealed record TaskEnvelope(TaskResp Task);

    private static async Task<ListResp> GetListAsync(HttpClient client, Guid listId)
        => (await client.GetFromJsonAsync<ListResp>($"/api/v1/lists/{listId}"))!;

    private static async Task<SchemeResp> CreateSchemeAsync(
        HttpClient client, string name, params (string Name, string Category)[] statuses)
        => await ReadAsync<SchemeResp>(await client.PostAsJsonAsync(
            "/api/v1/status-schemes",
            new { name, statuses = statuses.Select(s => new { name = s.Name, category = s.Category, color = (string?)null }) }));

    private static Task<SchemeResp> ReadSchemeAsync(HttpResponseMessage response) => ReadAsync<SchemeResp>(response);

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

internal sealed record SpaceSchemeResp(SchemeResp Scheme, bool IsCustomized);

internal static class DeleteWithBodyExtensions
{
    /// <summary>DELETE with a JSON body — <see cref="HttpClient.DeleteAsync(string?)"/> has no body overload,
    /// but the status-removal and Space-revert endpoints require one (the replacement status mapping).</summary>
    public static Task<HttpResponseMessage> DeleteAsJsonAsync<T>(this HttpClient client, string requestUri, T body)
        => client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, new Uri(requestUri, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        });
}
