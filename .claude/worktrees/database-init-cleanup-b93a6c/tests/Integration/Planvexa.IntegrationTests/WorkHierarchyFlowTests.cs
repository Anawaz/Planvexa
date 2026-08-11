namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

// Response shapes specific  endpoints.
internal sealed record ListWithDefaultViewResp(Guid Id, Guid SpaceId, Guid? FolderId, string Name, Guid StatusSchemeId, double Position, bool IsArchived, bool IsPrivate, Guid? DefaultViewId);
internal sealed record ViewResp(Guid Id, string Name, string ViewType, string ScopeType, Guid? ScopeId, string ConfigJson, bool IsPrivate);
internal sealed record TemplateResp(Guid Id, string ResourceType, string Name, DateTimeOffset CreatedAtUtc);
internal sealed record CreateFromTemplateResp(string ResourceType, Guid Id, string Name);
internal sealed record FavoriteResp(Guid Id, string ResourceType, Guid ResourceId, DateTimeOffset CreatedAtUtc);

/// <summary>Folder/List duplicate producing the correct structure via the real API.</summary>
[Collection("api")]
public sealed class WorkHierarchyDuplicateFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Duplicating_a_list_copies_its_tasks_and_preserves_subtask_relationships()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var parentTask = await client.CreateTaskAsync(list.Id, "Parent");
        await client.CreateTaskAsync(list.Id, "Child", parentTask.Id);
        await client.CreateTaskAsync(list.Id, "Standalone");

        var response = await client.PostAsync(new Uri($"/api/v1/lists/{list.Id}/duplicate", UriKind.Relative), content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var copy = (await response.Content.ReadFromJsonAsync<ListResp>())!;

        copy.Id.ShouldNotBe(list.Id);
        copy.Name.ShouldBe("Sprint 1 (Copy)");

        var copiedTasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{copy.Id}/tasks");
        copiedTasks!.Count.ShouldBe(3);

        var copiedParent = copiedTasks.Single(t => t.Title == "Parent");
        var copiedChild = copiedTasks.Single(t => t.Title == "Child");
        copiedChild.ParentId.ShouldBe(copiedParent.Id);
        copiedParent.Id.ShouldNotBe(parentTask.Id);
    }

    [Fact]
    public async Task Duplicating_a_folder_deep_copies_subfolders_and_lists()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var folder = await client.CreateFolderAsync(space.Id, "Sprint folder");
        await client.CreateListAsync(space.Id, "List A", folder.Id);
        await client.CreateListAsync(space.Id, "List B", folder.Id);

        var subResponse = await client.PostAsJsonAsync($"/api/v1/spaces/{space.Id}/folders", new { name = "Sub", parentFolderId = folder.Id });
        subResponse.EnsureSuccessStatusCode();
        var sub = (await subResponse.Content.ReadFromJsonAsync<FolderResp>())!;
        var subList = await client.CreateListAsync(space.Id, "Sub list", sub.Id);
        var subTask = await client.CreateTaskAsync(subList.Id, "Do it");

        var dupResponse = await client.PostAsync(new Uri($"/api/v1/folders/{folder.Id}/duplicate", UriKind.Relative), content: null);
        dupResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var copiedFolder = (await dupResponse.Content.ReadFromJsonAsync<FolderResp>())!;
        copiedFolder.Name.ShouldBe("Sprint folder (Copy)");

        var allFolders = await client.GetFromJsonAsync<List<FolderResp>>($"/api/v1/spaces/{space.Id}/folders");
        // Source: folder + sub = 2. Copy: copiedFolder + its own copied subfolder = 2. Total 4.
        allFolders!.Count.ShouldBe(4);

        var copiedSub = allFolders.Single(f => f.ParentFolderId == copiedFolder.Id);
        var allLists = await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{space.Id}/lists");
        // Source: List A, List B, Sub list = 3. Copy: same 3 = 6 total.
        allLists!.Count.ShouldBe(6);

        var copiedSubList = allLists.Single(l => l.FolderId == copiedSub.Id);
        var copiedSubListTasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{copiedSubList.Id}/tasks");
        copiedSubListTasks!.ShouldHaveSingleItem();
        copiedSubListTasks[0].Title.ShouldBe(subTask.Title);
        copiedSubListTasks[0].Id.ShouldNotBe(subTask.Id);
    }
}

/// <summary>Save-as-template + create-from-template round trip via the real API.</summary>
[Collection("api")]
public sealed class WorkTemplateFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task List_template_round_trips_status_scheme_and_custom_fields()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id, "Source list");

        var fieldResponse = await client.PostAsJsonAsync("/api/v1/custom-fields", new
        {
            name = "Severity",
            type = "Text",
            scope = "List",
            scopeId = list.Id,
            isRequired = false,
            options = Array.Empty<object>(),
        });
        fieldResponse.EnsureSuccessStatusCode();

        var saveResponse = await client.PostAsJsonAsync("/api/v1/templates", new
        {
            resourceType = "List",
            sourceResourceId = list.Id,
            name = "List template",
        });
        saveResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var template = (await saveResponse.Content.ReadFromJsonAsync<TemplateResp>())!;
        template.ResourceType.ShouldBe("List");

        var templates = await client.GetFromJsonAsync<List<TemplateResp>>("/api/v1/templates");
        templates!.ShouldContain(t => t.Id == template.Id);

        var applyResponse = await client.PostAsJsonAsync($"/api/v1/templates/{template.Id}/apply", new
        {
            spaceId = space.Id,
            name = "From template",
        });
        applyResponse.EnsureSuccessStatusCode();
        var applied = (await applyResponse.Content.ReadFromJsonAsync<CreateFromTemplateResp>())!;
        applied.Name.ShouldBe("From template");

        var newLists = await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{space.Id}/lists");
        var newList = newLists!.Single(l => l.Id == applied.Id);
        newList.StatusSchemeId.ShouldNotBe(list.StatusSchemeId);

        var newListFields = await client.GetFromJsonAsync<List<CustomFieldResp>>($"/api/v1/lists/{newList.Id}/custom-fields");
        newListFields!.ShouldContain(f => f.Name == "Severity" && f.Scope == "List" && f.ScopeId == newList.Id);
    }

    [Fact]
    public async Task Folder_template_round_trips_substructure_without_task_content()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var folder = await client.CreateFolderAsync(space.Id, "Source folder");
        var list = await client.CreateListAsync(space.Id, "Inner list", folder.Id);
        await client.CreateTaskAsync(list.Id, "Should not be copied");

        var saveResponse = await client.PostAsJsonAsync("/api/v1/templates", new
        {
            resourceType = "Folder",
            sourceResourceId = folder.Id,
            name = "Folder template",
        });
        saveResponse.EnsureSuccessStatusCode();
        var template = (await saveResponse.Content.ReadFromJsonAsync<TemplateResp>())!;

        var applyResponse = await client.PostAsJsonAsync($"/api/v1/templates/{template.Id}/apply", new
        {
            spaceId = space.Id,
            name = "Applied folder",
        });
        applyResponse.EnsureSuccessStatusCode();
        var applied = (await applyResponse.Content.ReadFromJsonAsync<CreateFromTemplateResp>())!;

        var newFolders = await client.GetFromJsonAsync<List<FolderResp>>($"/api/v1/spaces/{space.Id}/folders");
        var appliedFolder = newFolders!.Single(f => f.Id == applied.Id);
        appliedFolder.Name.ShouldBe("Applied folder");

        var newLists = await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{space.Id}/lists");
        var appliedList = newLists!.Single(l => l.FolderId == appliedFolder.Id);
        appliedList.Name.ShouldBe("Inner list");

        var appliedListTasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{appliedList.Id}/tasks");
        appliedListTasks.ShouldBeEmpty();
    }
}

/// <summary>Favourite/unfavourite toggling and listing via the real API.</summary>
[Collection("api")]
public sealed class WorkFavoriteFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Toggling_a_favorite_adds_then_removes_it()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();

        var firstToggle = await client.PostAsJsonAsync("/api/v1/favorites/toggle", new { resourceType = "space", resourceId = space.Id });
        firstToggle.EnsureSuccessStatusCode();
        (await firstToggle.Content.ReadFromJsonAsync<ToggleResp>())!.IsFavorited.ShouldBeTrue();

        var favorites = await client.GetFromJsonAsync<List<FavoriteResp>>("/api/v1/favorites");
        favorites!.ShouldContain(f => f.ResourceType == "space" && f.ResourceId == space.Id);

        var secondToggle = await client.PostAsJsonAsync("/api/v1/favorites/toggle", new { resourceType = "space", resourceId = space.Id });
        secondToggle.EnsureSuccessStatusCode();
        (await secondToggle.Content.ReadFromJsonAsync<ToggleResp>())!.IsFavorited.ShouldBeFalse();

        var favoritesAfter = await client.GetFromJsonAsync<List<FavoriteResp>>("/api/v1/favorites");
        favoritesAfter!.ShouldNotContain(f => f.ResourceType == "space" && f.ResourceId == space.Id);
    }

    private sealed record ToggleResp(bool IsFavorited);
}

/// <summary>A List's recorded default view via the real API.</summary>
[Collection("api")]
public sealed class WorkDefaultViewFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Setting_a_lists_default_view_is_persisted_and_returned()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var viewResponse = await client.PostAsJsonAsync("/api/v1/views", new
        {
            viewType = "Board",
            scopeType = "List",
            scopeId = list.Id,
            name = "Board view",
            config = "{}",
            isPrivate = false,
        });
        viewResponse.EnsureSuccessStatusCode();
        var view = (await viewResponse.Content.ReadFromJsonAsync<ViewResp>())!;

        var setResponse = await client.PutAsJsonAsync($"/api/v1/lists/{list.Id}/default-view", new { viewId = view.Id });
        setResponse.EnsureSuccessStatusCode();
        var updated = (await setResponse.Content.ReadFromJsonAsync<ListWithDefaultViewResp>())!;
        updated.DefaultViewId.ShouldBe(view.Id);

        var fetched = await client.GetFromJsonAsync<ListWithDefaultViewResp>($"/api/v1/lists/{list.Id}");
        fetched!.DefaultViewId.ShouldBe(view.Id);

        var cleared = await client.PutAsJsonAsync($"/api/v1/lists/{list.Id}/default-view", new { viewId = (Guid?)null });
        cleared.EnsureSuccessStatusCode();
        (await cleared.Content.ReadFromJsonAsync<ListWithDefaultViewResp>())!.DefaultViewId.ShouldBeNull();
    }
}
