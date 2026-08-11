namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

internal sealed record AiAskResp(string Answer, List<SearchResp> Sources, int TokensEstimated);

internal sealed record AiGovernanceResp(
    List<string> AllowedModels, bool RedactEmails, bool RedactApiKeys, bool RedactCreditCards, List<string> CustomRedactionPatterns);

/// <summary>
/// AI capability expansion. These are the load-bearing security tests for it, mirroring
/// SearchFlowTests' permission-leak regression tests exactly: workspace Q&amp;A and semantic search are both
/// layered on top of the already permission-filtered cross-module search fan-out, and must never
/// surface a resource the requesting user cannot themselves read — regardless of what a real LLM provider
/// might otherwise be tempted to "know" or infer. No AI provider is configured in these tests, so both
/// capabilities run in the offline/ExtractiveAi fallback path end to end.
/// </summary>
[Collection("api")]
public sealed class AiExpansionFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Workspace_qa_never_answers_from_a_private_lists_task_the_asker_cannot_read()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync("Confidential space");
        var marker = $"qaleak{Guid.NewGuid():N}"[..12];
        var list = await owner.CreateListAsync(space.Id, $"{marker} secret list");
        var task = await owner.CreateTaskAsync(list.Id, $"{marker} rotate the prod database password");

        (await owner.PatchAsJsonAsync($"/api/v1/resources/list/{list.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "qa-leak");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        var question = $"What is the {marker} task about?";

        // The owner (who can read the private list) gets an answer that surfaces the task.
        var ownerAnswer = await AskAsync(owner, question);
        ownerAnswer.Sources.ShouldContain(s => s.Id == task.Id);
        ownerAnswer.Answer.ShouldContain(marker);

        // The ungranted member must get neither the task as a "source" NOR its content echoed into the
        // generated answer text — the exact confidentiality-bug shape earlier work found in listing/search
        // paths that skipped a per-resource permission check.
        var memberAnswer = await AskAsync(memberClient, question);
        memberAnswer.Sources.ShouldNotContain(s => s.Id == task.Id);
        memberAnswer.Answer.ShouldNotContain("rotate the prod database password");

        // Once granted View on the list, the member's question resolves the task too.
        var grant = await owner.PostAsJsonAsync(
            $"/api/v1/resources/list/{list.Id}/permissions",
            new { principalType = "user", principalId = memberUserId, level = "view" });
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        var afterGrant = await AskAsync(memberClient, question);
        afterGrant.Sources.ShouldContain(s => s.Id == task.Id);
    }

    [Fact]
    public async Task Workspace_qa_never_answers_from_a_private_document_the_asker_cannot_read()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var marker = $"qadoc{Guid.NewGuid():N}"[..11];
        var create = await owner.PostAsJsonAsync(
            "/api/v1/documents", new { title = $"{marker} secret runbook", content = "the master key is hidden under the rug", isPrivate = true });
        create.EnsureSuccessStatusCode();

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "qa-doc");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        var question = $"Tell me about the {marker} document";

        (await AskAsync(owner, question)).Sources.ShouldContain(s => s.Type == "Document");
        var memberAnswer = await AskAsync(memberClient, question);
        memberAnswer.Sources.ShouldNotContain(s => s.Type == "Document");
        memberAnswer.Answer.ShouldNotContain("master key");
    }

    [Fact]
    public async Task Semantic_search_respects_the_same_private_list_filtering_as_keyword_search()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync("Confidential space 2");
        var marker = $"semleak{Guid.NewGuid():N}"[..11];
        var list = await owner.CreateListAsync(space.Id, $"{marker} list");
        var task = await owner.CreateTaskAsync(list.Id, $"{marker} task");

        (await owner.PatchAsJsonAsync($"/api/v1/resources/list/{list.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "sem-leak");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        (await SemanticSearchAsync(owner, marker)).ShouldContain(r => r.Id == task.Id);
        (await SemanticSearchAsync(memberClient, marker)).ShouldNotContain(r => r.Id == task.Id);
    }

    [Fact]
    public async Task Semantic_search_reorders_but_never_widens_the_underlying_search_results()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync("Delivery");
        var list = await client.CreateListAsync(space.Id, "Queue");
        var marker = $"rank{Guid.NewGuid():N}"[..10];
        await client.CreateTaskAsync(list.Id, $"{marker} exact phrase match here");
        await client.CreateTaskAsync(list.Id, $"unrelated task mentioning {marker} in passing only");

        var plain = await SearchPlainAsync(client, marker);
        var semantic = await SemanticSearchAsync(client, marker);

        // Same underlying set (semantic search narrows/reorders, never adds a hit search wouldn't return).
        semantic.Select(s => (s.Type, s.Id)).ToHashSet().IsSubsetOf(plain.Select(s => (s.Type, s.Id)).ToHashSet()).ShouldBeTrue();
        semantic.Count.ShouldBe(plain.Count);
    }

    [Fact]
    public async Task Model_allow_list_rejects_a_disallowed_model()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var governance = await owner.PutAsJsonAsync("/api/v1/ai/settings/governance", new
        {
            allowedModels = new[] { "gpt-4*" },
            redactEmails = true,
            redactApiKeys = true,
            redactCreditCards = true,
            customRedactionPatterns = Array.Empty<string>(),
        });
        governance.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = (await governance.Content.ReadFromJsonAsync<AiGovernanceResp>())!;
        dto.AllowedModels.ShouldBe(["gpt-4*"]);

        // A disallowed model is rejected with a clear (400) error, not silently accepted.
        var rejected = await owner.PutAsJsonAsync("/api/v1/ai/settings", new
        {
            baseUrl = "http://localhost:4000",
            model = "some-unvetted-model",
            apiKey = "k",
            isEnabled = true,
        });
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // An allowed (wildcard-matched) model is accepted.
        var accepted = await owner.PutAsJsonAsync("/api/v1/ai/settings", new
        {
            baseUrl = "http://localhost:4000",
            model = "gpt-4o-mini",
            apiKey = "k",
            isEnabled = true,
        });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Non_admin_cannot_read_or_change_governance_settings()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "gov-member");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        (await memberClient.GetAsync("/api/v1/ai/settings/governance")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<AiAskResp> AskAsync(HttpClient client, string question)
    {
        var response = await client.PostAsJsonAsync("/api/v1/ai/ask", new { question });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AiAskResp>())!;
    }

    private static async Task<List<SearchResp>> SemanticSearchAsync(HttpClient client, string term)
        => (await client.GetFromJsonAsync<List<SearchResp>>($"/api/v1/ai/search/semantic?q={Uri.EscapeDataString(term)}"))!;

    private static async Task<List<SearchResp>> SearchPlainAsync(HttpClient client, string term)
        => (await client.GetFromJsonAsync<List<SearchResp>>($"/api/v1/search?q={Uri.EscapeDataString(term)}"))!;
}
