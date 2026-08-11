namespace Planvexa.IntegrationTests;

using System.Net.Http.Json;
using Shouldly;
using Xunit;

internal sealed record RecentItemResp(string ResourceType, Guid ResourceId, DateTimeOffset ViewedAtUtc);

/// <summary>"recently viewed" tracking (upsert-on-view, workspace-scoped) via the real API.</summary>
[Collection("api")]
public sealed class RecentItemFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Recording_a_view_makes_it_the_most_recent_item()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id);

        (await client.PostAsJsonAsync("/api/v1/recent-items", new { resourceType = "task", resourceId = task.Id }))
            .EnsureSuccessStatusCode();

        var items = await client.GetFromJsonAsync<List<RecentItemResp>>("/api/v1/recent-items");
        items!.ShouldContain(i => i.ResourceType == "task" && i.ResourceId == task.Id);
    }

    [Fact]
    public async Task Viewing_the_same_resource_again_bumps_it_instead_of_duplicating()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var taskA = await client.CreateTaskAsync(list.Id, "Task A");
        var taskB = await client.CreateTaskAsync(list.Id, "Task B");

        (await client.PostAsJsonAsync("/api/v1/recent-items", new { resourceType = "task", resourceId = taskA.Id })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/recent-items", new { resourceType = "task", resourceId = taskB.Id })).EnsureSuccessStatusCode();

        // Re-viewing A moves it back to the front without adding a second row for it.
        (await client.PostAsJsonAsync("/api/v1/recent-items", new { resourceType = "task", resourceId = taskA.Id })).EnsureSuccessStatusCode();

        var items = (await client.GetFromJsonAsync<List<RecentItemResp>>("/api/v1/recent-items"))!;
        items.Count(i => i.ResourceType == "task" && i.ResourceId == taskA.Id).ShouldBe(1);
        items[0].ResourceId.ShouldBe(taskA.Id);
    }

    [Fact]
    public async Task Concurrent_views_of_the_same_resource_never_500_even_when_they_race()
    {
        // Reproduces a real bug found manually: two near-simultaneous "record this view" requests for the
        // same resource (e.g. rapid re-navigation firing the tracking call twice) raced past the
        // find-then-insert check in RecentItemService and the second one's insert hit the
        // (workspace, user, resource) unique constraint, surfacing as an unhandled 500 instead of the
        // idempotent no-op the caller expects.
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.PostAsJsonAsync("/api/v1/recent-items", new { resourceType = "task", resourceId = task.Id })));

        responses.ShouldAllBe(r => r.IsSuccessStatusCode);

        var items = (await client.GetFromJsonAsync<List<RecentItemResp>>("/api/v1/recent-items"))!;
        items.Count(i => i.ResourceType == "task" && i.ResourceId == task.Id).ShouldBe(1);
    }

    [Fact]
    public async Task Recent_items_are_isolated_per_workspace()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await clientA.CreateSpaceAsync();
        var listA = await clientA.CreateListAsync(spaceA.Id);
        var taskA = await clientA.CreateTaskAsync(listA.Id);
        (await clientA.PostAsJsonAsync("/api/v1/recent-items", new { resourceType = "task", resourceId = taskA.Id })).EnsureSuccessStatusCode();

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var itemsB = await clientB.GetFromJsonAsync<List<RecentItemResp>>("/api/v1/recent-items");
        itemsB!.ShouldNotContain(i => i.ResourceId == taskA.Id);
    }
}
