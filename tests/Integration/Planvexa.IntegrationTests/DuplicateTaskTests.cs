namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>Duplicating a task copies its title, assignees, and checklists.</summary>
[Collection("api")]
public sealed class DuplicateTaskTests(PlanvexaFixture fixture)
{
    private sealed record ChecklistResp(Guid Id, string Name, double Position, List<ChecklistItemResp> Items);
    private sealed record ChecklistItemResp(Guid Id, string Content, bool IsResolved, double Position);
    private sealed record TaskDetailResp(TaskResp Task, List<Guid> WatcherUserIds, List<ChecklistResp> Checklists);

    [Fact]
    public async Task Duplicate_copies_title_assignees_and_checklists()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Original");

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/users/me");
        (await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/assignees", new { userId = me!.UserId }))
            .EnsureSuccessStatusCode();

        var checklistResp = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/checklists", new { name = "Steps" });
        checklistResp.EnsureSuccessStatusCode();
        var checklist = (await checklistResp.Content.ReadFromJsonAsync<ChecklistResp>())!;
        (await client.PostAsJsonAsync($"/api/v1/checklists/{checklist.Id}/items", new { content = "Do it" }))
            .EnsureSuccessStatusCode();

        var dupResp = await client.PostAsync(new Uri($"/api/v1/tasks/{task.Id}/duplicate", UriKind.Relative), content: null);
        dupResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var copy = (await dupResp.Content.ReadFromJsonAsync<TaskResp>())!;

        copy.Id.ShouldNotBe(task.Id);
        copy.Title.ShouldBe("Original (Copy)");
        copy.AssigneeUserIds.ShouldContain(me.UserId);

        var detail = (await client.GetFromJsonAsync<TaskDetailResp>($"/api/v1/tasks/{copy.Id}"))!;
        detail.Checklists.ShouldHaveSingleItem();
        detail.Checklists[0].Name.ShouldBe("Steps");
        detail.Checklists[0].Items.ShouldHaveSingleItem();
        detail.Checklists[0].Items[0].Content.ShouldBe("Do it");
    }
}
