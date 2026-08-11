namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

internal sealed record SearchResp(string Type, Guid Id, string Title, string? Subtitle, Guid? ListId);

[Collection("api")]
public sealed class SearchFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Finds_a_task_by_partial_title()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync("Engineering");
        var list = await client.CreateListAsync(space.Id, "Backlog");
        var task = await client.CreateTaskAsync(list.Id, "Migrate the billing exporter");

        // Partial, case-insensitive, mid-word.
        var results = await SearchAsync(client, "BILLING expo");

        var hit = results.ShouldHaveSingleItem();
        hit.Type.ShouldBe("Task");
        hit.Id.ShouldBe(task.Id);
        hit.ListId.ShouldBe(list.Id);
        hit.Subtitle.ShouldBe("Backlog");
    }

    [Fact]
    public async Task Also_returns_matching_lists_and_spaces()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var marker = $"zeta{Guid.NewGuid():N}"[..12];
        var space = await client.CreateSpaceAsync($"{marker} space");
        var list = await client.CreateListAsync(space.Id, $"{marker} list");
        var task = await client.CreateTaskAsync(list.Id, $"{marker} task");

        var results = await SearchAsync(client, marker);

        results.Count.ShouldBe(3);

        // Jump targets first (space, then list), tasks after.
        results[0].Type.ShouldBe("Space");
        results[0].Id.ShouldBe(space.Id);
        results[1].Type.ShouldBe("List");
        results[1].Id.ShouldBe(list.Id);
        results[1].ListId.ShouldBe(list.Id);
        results[2].Type.ShouldBe("Task");
        results[2].Id.ShouldBe(task.Id);
    }

    [Fact]
    public async Task Does_not_return_another_tenants_task()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await clientA.CreateSpaceAsync();
        var listA = await clientA.CreateListAsync(spaceA.Id);
        var marker = $"xen{Guid.NewGuid():N}"[..11];
        await clientA.CreateTaskAsync(listA.Id, $"Secret {marker}");

        // Tenant A sees it...
        (await SearchAsync(clientA, marker)).Count.ShouldBe(1);

        // ...tenant B does not.
        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();
        (await SearchAsync(clientB, marker)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Returns_empty_for_a_single_character_term()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync("Alpha");
        var list = await client.CreateListAsync(space.Id, "Alpha list");
        await client.CreateTaskAsync(list.Id, "Alpha task");

        (await SearchAsync(client, "a")).ShouldBeEmpty();
        (await SearchAsync(client, "  ")).ShouldBeEmpty();
        (await SearchAsync(client, "al")).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Respects_the_limit()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync("Delivery");
        var list = await client.CreateListAsync(space.Id, "Queue");
        var marker = $"omk{Guid.NewGuid():N}"[..11];
        for (var i = 0; i < 4; i++)
        {
            await client.CreateTaskAsync(list.Id, $"{marker} task {i}");
        }

        (await SearchAsync(client, marker)).Count.ShouldBe(4);
        (await SearchAsync(client, marker, limit: 2)).Count.ShouldBe(2);

        // Out-of-range limits are clamped, not rejected.
        (await SearchAsync(client, marker, limit: 5000)).Count.ShouldBe(4);
    }

    // ---- cross-module search MUST permission-filter every result type it fans out to. ----
    // These are the load-bearing tests here: search aggregates across modules, so an unfiltered
    // result type here is a confidentiality bug (see SearchAggregator/ISearchProvider doc comments).

    [Fact]
    public async Task Private_list_and_its_task_are_hidden_from_an_ungranted_member_and_visible_once_granted()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync("Confidential space");
        var marker = $"priv{Guid.NewGuid():N}"[..11];
        var list = await owner.CreateListAsync(space.Id, $"{marker} list");
        var task = await owner.CreateTaskAsync(list.Id, $"{marker} task");

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "search-priv");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        // Visible to the member before the list is made private.
        (await SearchAsync(memberClient, marker)).ShouldNotBeEmpty();

        (await owner.PatchAsJsonAsync($"/api/v1/resources/list/{list.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The owner (creator) still sees both; the ungranted member sees neither the list nor the task inside it.
        var ownerResults = await SearchAsync(owner, marker);
        ownerResults.ShouldContain(r => r.Type == "List" && r.Id == list.Id);
        ownerResults.ShouldContain(r => r.Type == "Task" && r.Id == task.Id);
        (await SearchAsync(memberClient, marker)).ShouldBeEmpty();

        var grant = await owner.PostAsJsonAsync(
            $"/api/v1/resources/list/{list.Id}/permissions",
            new { principalType = "user", principalId = memberUserId, level = "view" });
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The ancestor-walk in IResourcePermissionQuery resolves the task's read access via its list's
        // grant, so both the list and the task it contains reappear once the member has View on the list.
        var afterGrant = await SearchAsync(memberClient, marker);
        afterGrant.ShouldContain(r => r.Type == "List" && r.Id == list.Id);
        afterGrant.ShouldContain(r => r.Type == "Task" && r.Id == task.Id);
    }

    [Fact]
    public async Task Private_document_is_hidden_from_search_for_a_non_owner_member()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var marker = $"docp{Guid.NewGuid():N}"[..11];
        var create = await owner.PostAsJsonAsync(
            "/api/v1/documents", new { title = $"{marker} secret doc", content = "classified", isPrivate = true });
        create.EnsureSuccessStatusCode();

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "search-doc");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        (await SearchAsync(owner, marker)).ShouldContain(r => r.Type == "Document");
        (await SearchAsync(memberClient, marker)).ShouldNotContain(r => r.Type == "Document");
    }

    [Fact]
    public async Task Chat_message_in_a_private_channel_is_hidden_from_a_non_member_and_visible_once_added()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var marker = $"chatp{Guid.NewGuid():N}"[..11];
        var create = await owner.PostAsJsonAsync("/api/v1/chat/channels", new { name = $"{marker} secret channel", isPrivate = true });
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelResp>())!;
        (await owner.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = $"{marker} the launch codes are 1234" }))
            .EnsureSuccessStatusCode();

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "search-chat");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        // Owner (a channel member) finds it; the non-member finds nothing at all for this marker.
        (await SearchAsync(owner, marker)).ShouldNotBeEmpty();
        (await SearchAsync(memberClient, marker)).ShouldBeEmpty();

        (await owner.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/members", new { userId = memberUserId }))
            .EnsureSuccessStatusCode();

        (await SearchAsync(memberClient, marker)).ShouldNotBeEmpty();
    }

    private static async Task<List<SearchResp>> SearchAsync(HttpClient client, string term, int? limit = null)
    {
        var query = $"/api/v1/search?q={Uri.EscapeDataString(term)}" + (limit is null ? string.Empty : $"&limit={limit}");
        return (await client.GetFromJsonAsync<List<SearchResp>>(query))!;
    }
}
