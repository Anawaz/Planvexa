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
    public async Task Automation_removes_a_tag_via_a_real_run()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var createTag = await client.PostAsJsonAsync("/api/v1/tags", new { name = "to-remove", color = (string?)null });
        createTag.StatusCode.ShouldBe(HttpStatusCode.Created);
        var tag = (await createTag.Content.ReadFromJsonAsync<TagResp>())!;

        // Rule: on task.created, remove the "to-remove" tag.
        var actionJson = "[{\"type\":\"remove_tag\",\"value\":\"to-remove\"}]";
        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Untag new tasks",
            triggerType = "task.created",
            conditionJson = "{}",
            actionJson,
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleResp>())!;

        // Create a task that already carries the tag → the outbox pipeline should dispatch the
        // automation and detach it.
        var createTaskResponse = await client.PostAsJsonAsync(
            "/api/v1/tasks", new { listId = list.Id, title = "Pre-tagged task", tagIds = new[] { tag.Id } });
        createTaskResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var task = (await createTaskResponse.Content.ReadFromJsonAsync<TaskResp>())!;
        task.TagIds.ShouldContain(tag.Id);

        var runs = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<AutomationRunResp>>($"/api/v1/automations/{rule.Id}/runs");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(40));

        runs.ShouldNotBeNull();
        runs!.ShouldContain(r => r.Status == "Success");

        // The action actually detached the tag from the task (not just recorded a run). GET /tasks/{id}
        // returns a TaskDetailDto (Task sub-object plus watchers/checklists/etc.), not the flat
        // TaskResp shape POST /tasks returns — reuse FormsCompletenessFlowTests' TaskDetailEnvelope shape.
        var updatedTask = await client.GetFromJsonAsync<TaskDetailEnvelope>($"/api/v1/tasks/{task.Id}");
        updatedTask!.Task.TagIds.ShouldNotContain(tag.Id);
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

    /// <summary>Picks a currently-free TCP port on loopback by binding to port 0 and reading it back —
    /// avoids a hardcoded port colliding with something else on the CI/dev machine.</summary>
    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Failed_webhook_delivery_can_be_manually_retried_and_succeeds()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        // No listener is bound yet, so the first delivery attempt fails and is logged (real socket-level
        // connection refusal, same as Webhook_delivery_is_attempted_and_logged_for_a_subscribed_event).
        var port = GetFreeTcpPort();
        var sinkUrl = $"http://127.0.0.1:{port}/planvexa-webhook-retry-sink";

        var createHook = await client.PostAsJsonAsync("/api/v1/webhooks", new
        {
            url = sinkUrl,
            eventTypes = new[] { "task.created" },
        });
        createHook.StatusCode.ShouldBe(HttpStatusCode.Created);
        var hook = (await createHook.Content.ReadFromJsonAsync<CreatedWebhookResp>())!;

        await client.CreateTaskAsync(list.Id, "Webhook retry task");

        var deliveries = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<WebhookDeliveryResp>>($"/api/v1/webhooks/{hook.Id}/deliveries");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(40));
        deliveries.ShouldNotBeNull();
        var failed = deliveries!.Single(d => d.EventType == "task.created");
        failed.Success.ShouldBeFalse();
        failed.Attempt.ShouldBe(1);

        // Now a real listener answers 200 on the same URL, and the delivery is retried.
        using var sink = new System.Net.HttpListener();
        sink.Prefixes.Add($"http://127.0.0.1:{port}/");
        sink.Start();
        var receiveTask = sink.GetContextAsync().ContinueWith(t =>
        {
            var context = t.Result;
            context.Response.StatusCode = 200;
            context.Response.Close();
        });

        var retry = await client.PostAsync(new Uri($"/api/v1/webhooks/{hook.Id}/deliveries/{failed.Id}/retry", UriKind.Relative), null);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        var retried = (await retry.Content.ReadFromJsonAsync<WebhookDeliveryResp>())!;
        retried.Success.ShouldBeTrue();
        retried.Attempt.ShouldBe(2);

        await receiveTask;
        sink.Stop();

        // The delivery log now reflects the retried outcome on the same row, not a second row.
        var afterRetry = await client.GetFromJsonAsync<List<WebhookDeliveryResp>>($"/api/v1/webhooks/{hook.Id}/deliveries");
        afterRetry!.Count(d => d.EventType == "task.created").ShouldBe(1);
    }

    [Fact]
    public async Task Non_admin_cannot_retry_a_webhook_delivery()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);

        var createHook = await owner.PostAsJsonAsync("/api/v1/webhooks", new
        {
            url = "http://127.0.0.1:59322/planvexa-webhook-sink",
            eventTypes = new[] { "task.created" },
        });
        createHook.StatusCode.ShouldBe(HttpStatusCode.Created);
        var hook = (await createHook.Content.ReadFromJsonAsync<CreatedWebhookResp>())!;

        await owner.CreateTaskAsync(list.Id, "Webhook auth task");

        var deliveries = await PollAsync(async () =>
        {
            var found = await owner.GetFromJsonAsync<List<WebhookDeliveryResp>>($"/api/v1/webhooks/{hook.Id}/deliveries");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(40));
        deliveries.ShouldNotBeNull();
        var delivery = deliveries!.Single(d => d.EventType == "task.created");

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "wr");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var retry = await member.PostAsync(new Uri($"/api/v1/webhooks/{hook.Id}/deliveries/{delivery.Id}/retry", UriKind.Relative), null);
        retry.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
    public async Task Scoped_pat_is_denied_outside_its_scope_and_allowed_within_it()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var existingTask = await client.CreateTaskAsync(list.Id, "Existing task");

        var createToken = await client.PostAsJsonAsync("/api/v1/tokens", new { name = "read-only", scopes = new[] { "tasks:read" } });
        createToken.StatusCode.ShouldBe(HttpStatusCode.Created);
        var token = (await createToken.Content.ReadFromJsonAsync<CreatedTokenResp>())!;

        var patClient = fixture.Factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        patClient.DefaultRequestHeaders.Add("X-Workspace", workspaceId.ToString());

        // Within its granted scope (tasks:read) the token succeeds.
        var read = await patClient.GetAsync(new Uri($"/api/v1/tasks/{existingTask.Id}", UriKind.Relative));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Outside its granted scope (tasks:write) the token is denied, default-deny, even though the
        // caller is a workspace member who could otherwise create tasks.
        var create = await patClient.PostAsJsonAsync("/api/v1/tasks", new { listId = list.Id, title = "Should be blocked" });
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
