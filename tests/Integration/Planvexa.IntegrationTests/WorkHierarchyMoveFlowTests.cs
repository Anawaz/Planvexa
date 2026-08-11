namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>
/// List Move/Copy between Spaces and Folders (spec sections 8/9: Lists support Move/Copy like every
/// other hierarchy level) — previously entirely missing (no domain mutator, no service method, no
/// endpoint; TaskList.FolderId had a private setter with zero callers).
/// </summary>
[Collection("api")]
public sealed class WorkHierarchyMoveFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Moving_a_list_to_a_different_space_updates_its_location_and_keeps_its_tasks()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await client.CreateSpaceAsync("Space A");
        var spaceB = await client.CreateSpaceAsync("Space B");
        var list = await client.CreateListAsync(spaceA.Id, "Roadmap");
        var task = await client.CreateTaskAsync(list.Id, "Keeps moving with the list");

        var response = await client.PostAsJsonAsync($"/api/v1/lists/{list.Id}/move", new { spaceId = spaceB.Id, folderId = (Guid?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var moved = (await response.Content.ReadFromJsonAsync<ListResp>())!;
        moved.SpaceId.ShouldBe(spaceB.Id);
        moved.FolderId.ShouldBeNull();

        // No longer listed under Space A; still has its task under Space B.
        (await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{spaceA.Id}/lists"))!
            .ShouldNotContain(l => l.Id == list.Id);
        (await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{spaceB.Id}/lists"))!
            .ShouldContain(l => l.Id == list.Id);
        var tasksAfterMove = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks");
        tasksAfterMove!.ShouldContain(t => t.Id == task.Id);
    }

    [Fact]
    public async Task Moving_a_list_into_a_folder_from_a_different_space_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await client.CreateSpaceAsync("Space A");
        var spaceB = await client.CreateSpaceAsync("Space B");
        var list = await client.CreateListAsync(spaceA.Id);
        var folderInB = await client.CreateFolderAsync(spaceB.Id, "Folder in B");

        // Target space is A (the folder's actual owner is B) — mismatched pair must be rejected.
        var response = await client.PostAsJsonAsync($"/api/v1/lists/{list.Id}/move", new { spaceId = spaceA.Id, folderId = folderInB.Id });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Copying_a_list_to_another_space_creates_an_independent_list_and_leaves_the_source_in_place()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await client.CreateSpaceAsync("Space A");
        var spaceB = await client.CreateSpaceAsync("Space B");
        var list = await client.CreateListAsync(spaceA.Id, "Backlog");
        await client.CreateTaskAsync(list.Id, "Task one");

        var response = await client.PostAsJsonAsync($"/api/v1/lists/{list.Id}/copy", new { spaceId = spaceB.Id, folderId = (Guid?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var copy = (await response.Content.ReadFromJsonAsync<ListResp>())!;

        copy.Id.ShouldNotBe(list.Id);
        copy.SpaceId.ShouldBe(spaceB.Id);
        copy.Name.ShouldBe("Backlog");

        // Source untouched, still in Space A with its own task.
        var sourceStillInA = await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{spaceA.Id}/lists");
        sourceStillInA!.ShouldContain(l => l.Id == list.Id);
        (await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks"))!.Count.ShouldBe(1);

        var copiedTasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{copy.Id}/tasks");
        copiedTasks!.Count.ShouldBe(1);
        copiedTasks[0].Id.ShouldNotBe((await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks"))![0].Id);
    }
}
