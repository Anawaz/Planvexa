namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

// Response shapes for the automation + integration endpoints.
internal sealed record AutomationRuleResp(Guid Id, string Name, string TriggerType, bool IsEnabled, string ConditionJson, string ActionJson);
internal sealed record AutomationRunResp(Guid Id, Guid RuleId, string Status, string? Detail, DateTimeOffset OccurredAtUtc);
internal sealed record CreatedWebhookResp(Guid Id, string Url, List<string> EventTypes, bool IsActive, DateTimeOffset CreatedAtUtc, string Secret);
internal sealed record WebhookResp(Guid Id, string Url, List<string> EventTypes, bool IsActive, DateTimeOffset CreatedAtUtc);
internal sealed record WebhookDeliveryResp(Guid Id, string EventType, int Attempt, bool Success, int? StatusCode, string? Detail, DateTimeOffset OccurredAtUtc);
internal sealed record CreatedTokenResp(Guid Id, string Name, List<string> Scopes, DateTimeOffset? ExpiresAtUtc, DateTimeOffset CreatedAtUtc, string Token);
internal sealed record TokenResp(Guid Id, string Name, List<string> Scopes, DateTimeOffset? LastUsedAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset CreatedAtUtc);
internal sealed record TagResp(Guid Id, string Name, string? Color);

[Collection("api")]
public sealed class AutomationsIntegrationsFlowTests(PlanvexaFixture fixture)
{
    /// <summary>Polls an async predicate until it returns a non-null result or the timeout elapses.</summary>
    private static async Task<T?> PollAsync<T>(Func<Task<T?>> probe, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(500);
        }

        return null;
    }

    [Fact]
    public async Task Automation_triggers_on_task_creation_and_records_a_successful_run()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        // Rule: on task.created, add the "auto-tagged" tag.
        var actionJson = "[{\"type\":\"add_tag\",\"value\":\"auto-tagged\"}]";
        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Tag new tasks",
            triggerType = "task.created",
            conditionJson = "{}",
            actionJson,
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleResp>())!;

        // Create a task → the outbox pipeline should dispatch the automation.
        await client.CreateTaskAsync(list.Id, "Triggering task");

        var runs = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<AutomationRunResp>>($"/api/v1/automations/{rule.Id}/runs");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(40));

        runs.ShouldNotBeNull();
        runs!.ShouldContain(r => r.Status == "Success");

        // The action created + applied the tag on the workspace.
        var tags = await client.GetFromJsonAsync<List<TagResp>>("/api/v1/tags");
        tags!.ShouldContain(t => t.Name == "auto-tagged");
    }

    [Fact]
    public async Task Webhook_delivery_is_attempted_and_logged_for_a_subscribed_event()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        // Subscribe to task.created. The sink is unreachable, so the delivery will be logged as failed —
        // which still proves the pipeline signed + attempted + recorded the delivery.
        var createHook = await client.PostAsJsonAsync("/api/v1/webhooks", new
        {
            url = "http://127.0.0.1:59321/planvexa-webhook-sink",
            eventTypes = new[] { "task.created" },
        });
        createHook.StatusCode.ShouldBe(HttpStatusCode.Created);
        var hook = (await createHook.Content.ReadFromJsonAsync<CreatedWebhookResp>())!;
        hook.Secret.ShouldNotBeNullOrWhiteSpace(); // secret returned once

        await client.CreateTaskAsync(list.Id, "Webhook task");

        var deliveries = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<WebhookDeliveryResp>>($"/api/v1/webhooks/{hook.Id}/deliveries");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(40));

        deliveries.ShouldNotBeNull();
        deliveries!.ShouldContain(d => d.EventType == "task.created");
    }

    [Fact]
    public async Task Personal_access_token_authenticates_api_requests()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();

        // Create a document so there is tenant-scoped data to read back through the PAT.
        await client.PostAsJsonAsync("/api/v1/documents", new { title = "PAT-visible", content = "x", isPrivate = false });

        var createToken = await client.PostAsJsonAsync("/api/v1/tokens", new { name = "ci", scopes = new[] { "read" } });
        createToken.StatusCode.ShouldBe(HttpStatusCode.Created);
        var token = (await createToken.Content.ReadFromJsonAsync<CreatedTokenResp>())!;
        token.Token.ShouldStartWith("pat_");

        // A fresh client with ONLY the bearer token + workspace header (no dev-subject).
        var patClient = fixture.Factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        patClient.DefaultRequestHeaders.Add("X-Workspace", workspaceId.ToString());

        var docs = await patClient.GetAsync(new Uri("/api/v1/documents", UriKind.Relative));
        docs.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await docs.Content.ReadFromJsonAsync<List<DocumentSummaryResp>>();
        list!.ShouldContain(d => d.Title == "PAT-visible");

        // The token now records a last-used timestamp.
        var tokens = await client.GetFromJsonAsync<List<TokenResp>>("/api/v1/tokens");
        tokens!.Single(t => t.Id == token.Id).LastUsedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Invalid_or_missing_pat_is_unauthorized()
    {
        var (_, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var patClient = fixture.Factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "pat_deadbeef");
        patClient.DefaultRequestHeaders.Add("X-Workspace", workspaceId.ToString());

        var docs = await patClient.GetAsync(new Uri("/api/v1/documents", UriKind.Relative));
        docs.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Automation_and_webhook_management_require_admin()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "am");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var rule = await member.PostAsJsonAsync("/api/v1/automations", new { name = "x", triggerType = "task.created", conditionJson = "{}", actionJson = "[]" });
        rule.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var hook = await member.PostAsJsonAsync("/api/v1/webhooks", new { url = "https://example.com/h", eventTypes = new[] { "task.created" } });
        hook.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // But a member may manage their own personal access tokens.
        var token = await member.PostAsJsonAsync("/api/v1/tokens", new { name = "mine", scopes = new[] { "read" } });
        token.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Webhooks_are_isolated_between_tenants()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        await clientA.PostAsJsonAsync("/api/v1/webhooks", new { url = "https://a.example.com/h", eventTypes = new[] { "task.created" } });

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var list = await clientB.GetFromJsonAsync<List<WebhookResp>>("/api/v1/webhooks");
        list!.ShouldNotContain(w => w.Url == "https://a.example.com/h");
    }
}
