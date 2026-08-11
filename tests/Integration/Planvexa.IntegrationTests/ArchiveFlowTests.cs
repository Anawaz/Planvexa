namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>
/// Archive/unarchive across the hierarchy (spec sections 7-10 list Archive/Restore for every level).
/// Space and List already had ArchiveAsync(archive: bool) in the service, but no endpoint ever passed
/// false — archiving was a one-way door via the API. Task had no archive concept at all.
/// </summary>
[Collection("api")]
public sealed class ArchiveFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Task_can_be_archived_and_unarchived()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Archive me");

        var archiveResp = await client.PostAsync(new Uri($"/api/v1/tasks/{task.Id}/archive", UriKind.Relative), null);
        archiveResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var archived = (await archiveResp.Content.ReadFromJsonAsync<TaskDto2>())!;
        archived.IsArchived.ShouldBeTrue();

        var unarchiveResp = await client.PostAsync(new Uri($"/api/v1/tasks/{task.Id}/unarchive", UriKind.Relative), null);
        unarchiveResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unarchived = (await unarchiveResp.Content.ReadFromJsonAsync<TaskDto2>())!;
        unarchived.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public async Task List_can_be_unarchived_after_being_archived()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        (await client.PostAsync(new Uri($"/api/v1/lists/{list.Id}/archive", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var archived = await client.GetFromJsonAsync<ListWithDefaultViewResp>($"/api/v1/lists/{list.Id}");
        archived!.IsArchived.ShouldBeTrue();

        (await client.PostAsync(new Uri($"/api/v1/lists/{list.Id}/unarchive", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var unarchived = await client.GetFromJsonAsync<ListWithDefaultViewResp>($"/api/v1/lists/{list.Id}");
        unarchived!.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public async Task Folder_can_be_archived_restored_and_reordered()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var folder = await client.CreateFolderAsync(space.Id);

        (await client.PostAsync(new Uri($"/api/v1/folders/{folder.Id}/archive", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<FolderResp>($"/api/v1/folders/{folder.Id}"))!.IsArchived.ShouldBeTrue();

        (await client.PostAsync(new Uri($"/api/v1/folders/{folder.Id}/unarchive", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<FolderResp>($"/api/v1/folders/{folder.Id}"))!.IsArchived.ShouldBeFalse();

        var deleteResp = await client.DeleteAsync(new Uri($"/api/v1/folders/{folder.Id}", UriKind.Relative));
        deleteResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var restoreResp = await client.PostAsync(new Uri($"/api/v1/folders/{folder.Id}/restore", UriKind.Relative), null);
        restoreResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<FolderResp>($"/api/v1/folders/{folder.Id}"))!.Id.ShouldBe(folder.Id);

        var reorderResp = await client.PostAsJsonAsync($"/api/v1/folders/{folder.Id}/reorder", new { position = 4096.0 });
        reorderResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reordered = (await reorderResp.Content.ReadFromJsonAsync<FolderResp>())!;
        reordered.Position.ShouldBe(4096.0);
    }

    [Fact]
    public async Task Space_can_be_unarchived_after_being_archived()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();

        (await client.PostAsync(new Uri($"/api/v1/spaces/{space.Id}/archive", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<SpaceResp>>("/api/v1/spaces"))!
            .Single(s => s.Id == space.Id).IsArchived.ShouldBeTrue();

        (await client.PostAsync(new Uri($"/api/v1/spaces/{space.Id}/unarchive", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<SpaceResp>>("/api/v1/spaces"))!
            .Single(s => s.Id == space.Id).IsArchived.ShouldBeFalse();
    }
}
