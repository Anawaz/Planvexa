namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>Arbitrary-depth folder nesting under a Space, with cycle prevention on re-parenting.</summary>
[Collection("api")]
public sealed class FolderNestingTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Subfolder_is_created_under_a_top_level_folder()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();

        var parent = await CreateFolderAsync(client, space.Id, "Parent");
        parent.ParentFolderId.ShouldBeNull();

        var child = await CreateFolderAsync(client, space.Id, "Child", parent.Id);
        child.ParentFolderId.ShouldBe(parent.Id);

        var folders = await client.GetFromJsonAsync<List<FolderResp>>($"/api/v1/spaces/{space.Id}/folders");
        folders!.Count.ShouldBe(2);
        folders.Single(f => f.Id == child.Id).ParentFolderId.ShouldBe(parent.Id);
    }

    [Fact]
    public async Task Folders_nest_to_arbitrary_depth()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var parent = await CreateFolderAsync(client, space.Id, "Parent");
        var child = await CreateFolderAsync(client, space.Id, "Child", parent.Id);

        var grandchild = await CreateFolderAsync(client, space.Id, "Grandchild", child.Id);
        grandchild.ParentFolderId.ShouldBe(child.Id);

        var greatGrandchild = await CreateFolderAsync(client, space.Id, "Great-grandchild", grandchild.Id);
        greatGrandchild.ParentFolderId.ShouldBe(grandchild.Id);

        var folders = await client.GetFromJsonAsync<List<FolderResp>>($"/api/v1/spaces/{space.Id}/folders");
        folders!.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Moving_a_folder_under_its_own_descendant_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var root = await CreateFolderAsync(client, space.Id, "Root");
        var child = await CreateFolderAsync(client, space.Id, "Child", root.Id);
        var grandchild = await CreateFolderAsync(client, space.Id, "Grandchild", child.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/folders/{root.Id}/move", new { parentFolderId = grandchild.Id });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The attempted move did not take effect.
        var folders = await client.GetFromJsonAsync<List<FolderResp>>($"/api/v1/spaces/{space.Id}/folders");
        folders!.Single(f => f.Id == root.Id).ParentFolderId.ShouldBeNull();
    }

    [Fact]
    public async Task Moving_a_folder_under_itself_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var folder = await CreateFolderAsync(client, space.Id, "Solo");

        var response = await client.PostAsJsonAsync($"/api/v1/folders/{folder.Id}/move", new { parentFolderId = folder.Id });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Moving_a_folder_to_an_unrelated_folder_succeeds()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var a = await CreateFolderAsync(client, space.Id, "A");
        var b = await CreateFolderAsync(client, space.Id, "B");

        var response = await client.PostAsJsonAsync($"/api/v1/folders/{a.Id}/move", new { parentFolderId = b.Id });
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<FolderResp>())!.ParentFolderId.ShouldBe(b.Id);
    }

    [Fact]
    public async Task Parent_folder_from_another_space_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await client.CreateSpaceAsync("A");
        var spaceB = await client.CreateSpaceAsync("B");
        var parentInA = await CreateFolderAsync(client, spaceA.Id, "In A");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/spaces/{spaceB.Id}/folders", new { name = "X", parentFolderId = parentInA.Id });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Folder_can_be_renamed_and_deleted_when_empty()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var folder = await CreateFolderAsync(client, space.Id, "Old name");

        var renamed = await client.PatchAsJsonAsync($"/api/v1/folders/{folder.Id}", new { name = "New name" });
        renamed.EnsureSuccessStatusCode();
        (await renamed.Content.ReadFromJsonAsync<FolderResp>())!.Name.ShouldBe("New name");

        (await client.DeleteAsync(new Uri($"/api/v1/folders/{folder.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var folders = await client.GetFromJsonAsync<List<FolderResp>>($"/api/v1/spaces/{space.Id}/folders");
        folders!.ShouldNotContain(f => f.Id == folder.Id);
    }

    [Fact]
    public async Task Deleting_a_non_empty_folder_is_blocked()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var withList = await CreateFolderAsync(client, space.Id, "Has a list");
        (await client.PostAsJsonAsync("/api/v1/lists", new { spaceId = space.Id, folderId = withList.Id, name = "L" }))
            .EnsureSuccessStatusCode();
        (await client.DeleteAsync(new Uri($"/api/v1/folders/{withList.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var withSub = await CreateFolderAsync(client, space.Id, "Has a subfolder");
        await CreateFolderAsync(client, space.Id, "Sub", withSub.Id);
        (await client.DeleteAsync(new Uri($"/api/v1/folders/{withSub.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static async Task<FolderResp> CreateFolderAsync(HttpClient client, Guid spaceId, string name, Guid? parentFolderId = null)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/spaces/{spaceId}/folders", new { name, parentFolderId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FolderResp>())!;
    }
}

internal sealed record FolderResp(Guid Id, Guid SpaceId, Guid? ParentFolderId, string Name, double Position);
