namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Automations.Application;
using Planvexa.Modules.Automations.Application.Services;
using Planvexa.SharedContracts.Events;
using Shouldly;
using Xunit;

internal sealed record AutomationRuleFullResp(
    Guid Id, string Name, string TriggerType, bool IsEnabled, string ConditionJson, string ActionJson,
    string? TriggerConfigJson, int Version);

internal sealed record AutomationRunFullResp(
    Guid Id, Guid RuleId, string Status, string? Detail, DateTimeOffset OccurredAtUtc, int Attempts, DateTimeOffset? NextRetryAtUtc);

internal sealed record AutomationTemplateResp(string Key, string Name, string Description, string TriggerType, string ConditionJson, string ActionJson);
internal sealed record AutomationRuleVersionResp(int Version, string Name, string TriggerType, string ConditionJson, string ActionJson, string? TriggerConfigJson, Guid ChangedByUserId, DateTimeOffset ChangedAtUtc);
internal sealed record AutomationDryRunResp(bool ConditionsMatched, List<string> WouldExecute);
internal sealed record CommentDtoResp(Guid Id, Guid TaskId, Guid? ParentId, Guid AuthorUserId, string Body, bool IsEdited, bool IsDeleted, List<Guid> MentionUserIds, List<object> Reactions, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, List<CommentDtoResp> Replies);
internal sealed record CustomFieldFullResp(Guid Id, string Name, string Type, string Scope, Guid? ScopeId, bool IsRequired);

/// <summary>
/// Automations expansion: new trigger types (scheduled/due-date/SLA, all sweep-driven — no
/// discrete event fires on its own), nested condition groups, new action types (email/webhook/
/// custom_field/comment), business-day date math, templates, versioning, dry-run, and bounded
/// retry-then-dead-letter. Background sweeps are invoked directly via their Runner classes (same pattern
/// as GoalsAndReportingFlowTests' RunScheduledReportOnceAsync) rather than waiting on the real
/// poll loop — a 15-minute/1-minute interval is not something a test should sit through.
/// </summary>
[Collection("api")]
public sealed class AutomationsExpansionTests(PlanvexaFixture fixture)
{
    private static readonly Guid PlatformSystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

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

    // ---- Nested condition groups + the "scheduled" trigger's sweep ----

    [Fact]
    public async Task Scheduled_trigger_fires_via_the_sweep_and_is_idempotent_within_the_same_slot()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();

        // A schedule.recurring event has no triggering task (EntityType is "AutomationRule" — see
        // WorkspaceEvent.Types.ScheduleRecurring's doc comment), so its action must be one that doesn't
        // need a task id — "notify" fits (it only needs a recipient guid, never validated against a real
        // task) whereas a task-targeting action like add_tag would silently no-op here.
        var recipientId = Guid.NewGuid();
        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Every minute notifier",
            triggerType = "schedule.recurring",
            conditionJson = "{}",
            actionJson = $$"""[{"type":"notify","value":"{{recipientId}}"}]""",
            triggerConfigJson = """{"everyMinutes":60}""",
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;
        rule.Version.ShouldBe(1);

        await RunScheduledSweepOnceAsync(workspaceId, rule.Id);

        var runsAfterFirst = await client.GetFromJsonAsync<List<AutomationRunFullResp>>($"/api/v1/automations/{rule.Id}/runs");
        runsAfterFirst!.ShouldContain(r => r.Status == "Success" && r.Detail != null && r.Detail.Contains("notify=ok"));

        // Same time-slot (everyMinutes=60, no time has passed) -> the deterministic event id already has
        // a recorded run, so a second sweep tick must NOT record a second run.
        await RunScheduledSweepOnceAsync(workspaceId, rule.Id);
        var runsAfterSecond = await client.GetFromJsonAsync<List<AutomationRunFullResp>>($"/api/v1/automations/{rule.Id}/runs");
        runsAfterSecond!.Count.ShouldBe(runsAfterFirst!.Count);
    }

    [Fact]
    public async Task Due_date_trigger_fires_for_a_task_due_today_via_the_sweep()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Due today");

        (await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { dueDate = DateTimeOffset.UtcNow }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Nested condition group: due today or overdue by up to 1 day.
        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Tag due tasks",
            triggerType = "task.due_soon",
            conditionJson = """{"and":[{"field":"daysUntilDue","gte":"-1"},{"field":"daysUntilDue","lte":"0"}]}""",
            actionJson = """[{"type":"add_tag","value":"due-alert"}]""",
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        await RunDueDateSweepOnceAsync(workspaceId);

        var runs = await client.GetFromJsonAsync<List<AutomationRunFullResp>>($"/api/v1/automations/{rule.Id}/runs");
        runs!.ShouldContain(r => r.Status == "Success");
        var tags = await client.GetFromJsonAsync<List<TagResp>>("/api/v1/tags");
        tags!.ShouldContain(t => t.Name == "due-alert");
    }

    [Fact]
    public async Task Sla_trigger_fires_when_a_task_has_been_in_status_longer_than_the_threshold()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Stuck task");

        // No status_changed activity yet -> "entered current status" falls back to task creation time.
        // Backdate the task's creation so it already breaches a 1-minute threshold.
        await BackdateTaskCreationAsync(task.Id, TimeSpan.FromMinutes(5));

        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "SLA breach",
            triggerType = "task.sla_breached",
            conditionJson = """{"field":"minutesInStatus","gte":"1"}""",
            actionJson = """[{"type":"add_tag","value":"sla-breach"}]""",
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        await RunSlaSweepOnceAsync(workspaceId);

        var runs = await client.GetFromJsonAsync<List<AutomationRunFullResp>>($"/api/v1/automations/{rule.Id}/runs");
        runs!.ShouldContain(r => r.Status == "Success");
        var tags = await client.GetFromJsonAsync<List<TagResp>>("/api/v1/tags");
        tags!.ShouldContain(t => t.Name == "sla-breach");
    }

    // ---- Recursion protection for the new-trigger-type loop scenario (item 10) ----

    [Fact]
    public async Task Recursion_guard_prevents_a_scheduled_rules_action_from_retriggering_a_status_change_rule()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Loop bait");
        var schemes = await client.GetSchemesAsync();
        var targetStatus = schemes.Single(s => s.IsDefault).Statuses.First(s => s.Name != "To Do");

        // Rule A (scheduled trigger type): a scheduled rule whose action changes status — this is the new
        // "automation-triggered-by-a-sweep, whose OWN action fires a real event" shape item 10 calls for.
        var ruleA = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Scheduled status flip",
            triggerType = "schedule.recurring",
            conditionJson = "{}",
            actionJson = $$"""[{"type":"set_status","value":"{{targetStatus.Name}}"}]""",
            triggerConfigJson = """{"everyMinutes":60}""",
        });
        ruleA.StatusCode.ShouldBe(HttpStatusCode.Created);
        var ruleADto = (await ruleA.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        // Rule B: reacts to task.status_changed. If the recursion guard failed, Rule A's system-actor
        // status change would re-trigger Rule B.
        var ruleB = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Reacts to status change",
            triggerType = "task.status_changed",
            conditionJson = "{}",
            actionJson = """[{"type":"add_tag","value":"loop-should-not-fire"}]""",
        });
        ruleB.StatusCode.ShouldBe(HttpStatusCode.Created);

        await RunScheduledSweepOnceAsync(workspaceId, ruleADto.Id);

        // Rule A's own run recorded successfully (the set_status action itself is not blocked).
        var runsA = await client.GetFromJsonAsync<List<AutomationRunFullResp>>($"/api/v1/automations/{ruleADto.Id}/runs");
        runsA!.ShouldContain(r => r.Status == "Success");

        // The real TaskStatusChangedIntegrationEvent flows through the ordinary outbox pipeline
        // (asynchronous) — poll for it, then assert the loop tag was never applied.
        await Task.Delay(TimeSpan.FromSeconds(8));
        var tags = await client.GetFromJsonAsync<List<TagResp>>("/api/v1/tags");
        tags!.ShouldNotContain(t => t.Name == "loop-should-not-fire");
    }

    // ---- Retries and dead-letter ----

    [Fact]
    public async Task Failed_run_due_for_retry_is_dead_lettered_once_its_rule_is_disabled()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();

        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Will be disabled",
            triggerType = "task.created",
            conditionJson = "{}",
            actionJson = """[{"type":"add_tag","value":"x"}]""",
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        // Force a Failed run into existence (simulating an action that threw for a transient reason —
        // none of this codebase's actions throw for a reproducible configuration, so the run's initial
        // Failed state is seeded directly, exactly the state AutomationDispatcher.DispatchAsync leaves
        // behind after a genuine exception) with its retry already due.
        var runId = await SeedFailedRunAsync(workspaceId, rule.Id);

        // The failure persists because the rule is disabled before the retry runs — RetryAsync's
        // "rule missing/disabled" path forces immediate dead-letter (see AutomationDispatcher.RetryAsync).
        (await client.PostAsync(new Uri($"/api/v1/automations/{rule.Id}/disable", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await RetryOneDueRunAsync(workspaceId, runId);

        var deadLetters = await client.GetFromJsonAsync<List<AutomationRunFullResp>>("/api/v1/automations/dead-letters");
        deadLetters!.ShouldContain(r => r.Id == runId);
    }

    [Fact]
    public async Task Manual_retry_endpoint_rearms_a_dead_lettered_run()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Dead rule",
            triggerType = "task.created",
            conditionJson = "{}",
            actionJson = "[]",
        });
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;
        var runId = await SeedFailedRunAsync(workspaceId, rule.Id);
        await ForceDeadLetterAsync(runId);

        var retryResp = await client.PostAsync(new Uri($"/api/v1/automations/runs/{runId}/retry", UriKind.Relative), null);
        retryResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rearmed = (await retryResp.Content.ReadFromJsonAsync<AutomationRunFullResp>())!;
        rearmed.Status.ShouldBe("Failed");
        rearmed.NextRetryAtUtc.ShouldNotBeNull();

        var deadLetters = await client.GetFromJsonAsync<List<AutomationRunFullResp>>("/api/v1/automations/dead-letters");
        deadLetters!.ShouldNotContain(r => r.Id == runId);
    }

    // ---- New actions: email/comment/webhook/custom_field, and the security scope requirement ----

    [Fact]
    public async Task Email_action_only_sends_to_a_current_workspace_member_never_an_arbitrary_id()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(client, workspaceId, "recipient");
        _ = memberSubject;
        var outsiderUserId = Guid.NewGuid();

        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Confidential task");

        var memberActionJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new
            {
                type = "email",
                value = System.Text.Json.JsonSerializer.Serialize(new
                {
                    recipientUserId = memberUserId.ToString(),
                    subject = "New: {{task.title}}",
                    body = "see {{task.title}}",
                }),
            },
        });
        var ruleToMember = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Email member",
            triggerType = "task.status_changed",
            conditionJson = "{}",
            actionJson = memberActionJson,
        });
        ruleToMember.StatusCode.ShouldBe(HttpStatusCode.Created);
        var memberRule = (await ruleToMember.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        var outsiderActionJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new
            {
                type = "email",
                value = System.Text.Json.JsonSerializer.Serialize(new
                {
                    recipientUserId = outsiderUserId.ToString(),
                    subject = "leak",
                    body = "leak",
                }),
            },
        });
        var ruleToOutsider = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Email outsider",
            triggerType = "task.assigned",
            conditionJson = "{}",
            actionJson = outsiderActionJson,
        });
        ruleToOutsider.StatusCode.ShouldBe(HttpStatusCode.Created);
        var outsiderRule = (await ruleToOutsider.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        // Dispatched directly under a scope bound to the workspace (same pattern as the sweep helpers
        // below) rather than through the real outbox pipeline: this test is about the email action's
        // membership-scoping LOGIC, not about outbox delivery timing (already covered by the original
        // original "Automation_triggers_on_task_creation" test). A real (non-system) actor is used since
        // task.status_changed/task.assigned are ordinary user-driven events, not sweep-synthesized ones.
        var realActorId = Guid.NewGuid();
        await DispatchDirectAsync(workspaceId, WorkspaceEvent.Types.TaskStatusChanged, "Task", task.Id, realActorId, new Dictionary<string, string>());

        var memberRuns = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<AutomationRunFullResp>>($"/api/v1/automations/{memberRule.Id}/runs");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(10));
        if (memberRuns is null || !memberRuns.Any(r => r.Status == "Success" && r.Detail != null && r.Detail.Contains("email=ok")))
        {
            throw new Exception($"Unexpected run detail(s): {string.Join(" | ", memberRuns?.Select(r => $"{r.Status}:{r.Detail}") ?? [])}");
        }

        var emailLog = fixture.Factory.Services.GetRequiredService<Planvexa.Api.Notifications.SentEmailLog>();
        emailLog.ForRecipient(memberUserId).ShouldContain(e => e.Subject.Contains("Confidential task"));

        // Dispatch the outsider-targeted rule too (task.assigned).
        await DispatchDirectAsync(workspaceId, WorkspaceEvent.Types.TaskAssigned, "Task", task.Id, realActorId, new Dictionary<string, string> { ["assigneeUserId"] = memberUserId.ToString() });

        var outsiderRuns = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<AutomationRunFullResp>>($"/api/v1/automations/{outsiderRule.Id}/runs");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(10));
        outsiderRuns.ShouldNotBeNull();

        // The action executed (Success) but the email action itself no-op'd — the recipient is not a
        // workspace member, so nothing was sent. This is the confidentiality-bug shape the roadmap warns
        // about: a system-actor action must not be usable to exfiltrate task details to an arbitrary id.
        outsiderRuns!.ShouldContain(r => r.Detail != null && r.Detail.Contains("email=noop"));
        emailLog.ForRecipient(outsiderUserId).ShouldBeEmpty();
    }

    [Fact]
    public async Task Comment_action_posts_a_visible_comment_on_the_triggering_task_only()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Comment on creation",
            triggerType = "task.created",
            conditionJson = "{}",
            actionJson = """[{"type":"comment","value":"Automation says hi"}]""",
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        var task = await client.CreateTaskAsync(list.Id, "Gets a comment");

        var comments = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<CommentDtoResp>>($"/api/v1/tasks/{task.Id}/comments");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(40));

        comments.ShouldNotBeNull();
        comments!.ShouldContain(c => c.Body == "Automation says hi");

        // Disable the rule before creating a second task: proves the comment only ever lands on the
        // SPECIFIC task that triggered the matching event, not on every task in the workspace.
        (await client.PostAsync(new Uri($"/api/v1/automations/{rule.Id}/disable", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var otherTask = await client.CreateTaskAsync(list.Id, "Gets nothing");
        await Task.Delay(TimeSpan.FromSeconds(2));

        var otherComments = await client.GetFromJsonAsync<List<CommentDtoResp>>($"/api/v1/tasks/{otherTask.Id}/comments");
        otherComments!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Webhook_action_fires_an_ad_hoc_signed_call_through_the_integrations_pipeline()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Ad hoc webhook",
            triggerType = "task.created",
            conditionJson = "{}",
            actionJson = """[{"type":"webhook","value":"{\"url\":\"http://127.0.0.1:59322/planvexa-automation-webhook-sink\"}"}]""",
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        await client.CreateTaskAsync(list.Id, "Webhook task");

        // The sink is unreachable, so the action itself reports noop — proving the pipeline attempted a
        // signed send (not that the request "succeeded"), same shape as the subscribed-webhook
        // test.
        var runs = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<AutomationRunFullResp>>($"/api/v1/automations/{rule.Id}/runs");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(40));
        runs.ShouldNotBeNull();
        runs!.ShouldContain(r => r.Detail != null && r.Detail.Contains("webhook="));
    }

    [Fact]
    public async Task Custom_field_action_sets_the_tasks_field_value()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var fieldResp = await client.PostAsJsonAsync("/api/v1/custom-fields", new { name = "Risk", type = "Text", scope = "Workspace" });
        fieldResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var field = (await fieldResp.Content.ReadFromJsonAsync<CustomFieldFullResp>())!;

        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Tag risk field",
            triggerType = "task.created",
            conditionJson = "{}",
            actionJson = $$"""[{"type":"custom_field","value":"{\"fieldId\":\"{{field.Id}}\",\"value\":\"High\"}"}]""",
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        var task = await client.CreateTaskAsync(list.Id, "Needs risk rating");

        var runs = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<AutomationRunFullResp>>($"/api/v1/automations/{rule.Id}/runs");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(40));
        runs.ShouldNotBeNull();
        runs!.ShouldContain(r => r.Status == "Success" && r.Detail != null && r.Detail.Contains("custom_field=ok"));

        var taskDetail = await client.GetFromJsonAsync<TaskResp>($"/api/v1/tasks/{task.Id}");
        taskDetail.ShouldNotBeNull();
    }

    // ---- Templates, versioning, dry-run ----

    [Fact]
    public async Task Templates_can_be_listed_and_instantiated_into_a_real_rule()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var templates = await client.GetFromJsonAsync<List<AutomationTemplateResp>>("/api/v1/automations/templates");
        templates.ShouldNotBeNull();
        templates!.ShouldNotBeEmpty();
        var template = templates.First();

        var instantiate = await client.PostAsync(new Uri($"/api/v1/automations/templates/{template.Key}/instantiate", UriKind.Relative), null);
        instantiate.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await instantiate.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;
        rule.Name.ShouldBe(template.Name);
        rule.TriggerType.ShouldBe(template.TriggerType);
    }

    [Fact]
    public async Task Editing_a_rule_records_a_version_and_it_can_be_reverted()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "v1 name",
            triggerType = "task.created",
            conditionJson = "{}",
            actionJson = "[]",
        });
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        (await client.PatchAsJsonAsync($"/api/v1/automations/{rule.Id}", new { name = "v2 name" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var versions = await client.GetFromJsonAsync<List<AutomationRuleVersionResp>>($"/api/v1/automations/{rule.Id}/versions");
        versions.ShouldNotBeNull();
        versions!.ShouldContain(v => v.Version == 1 && v.Name == "v1 name");

        var revert = await client.PostAsync(new Uri($"/api/v1/automations/{rule.Id}/versions/1/revert", UriKind.Relative), null);
        revert.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reverted = (await revert.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;
        reverted.Name.ShouldBe("v1 name");
        reverted.Version.ShouldBe(3); // create=1, rename=2, revert=3.
    }

    [Fact]
    public async Task Dry_run_reports_predicted_actions_without_any_side_effect()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Untouched by dry-run");

        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Dry run me",
            triggerType = "task.status_changed",
            conditionJson = """{"field":"toStatusId","equals":"target-status"}""",
            actionJson = """[{"type":"add_tag","value":"dry-run-tag"}]""",
        });
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        var matchResp = await client.PostAsJsonAsync($"/api/v1/automations/{rule.Id}/dry-run", new
        {
            sampleEventData = new Dictionary<string, string> { ["toStatusId"] = "target-status" },
            sampleTaskId = task.Id,
        });
        matchResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var matched = (await matchResp.Content.ReadFromJsonAsync<AutomationDryRunResp>())!;
        matched.ConditionsMatched.ShouldBeTrue();
        matched.WouldExecute.ShouldContain(s => s.Contains("add_tag") && s.Contains("Untouched by dry-run"));

        var noMatchResp = await client.PostAsJsonAsync($"/api/v1/automations/{rule.Id}/dry-run", new
        {
            sampleEventData = new Dictionary<string, string> { ["toStatusId"] = "some-other-status" },
            sampleTaskId = (Guid?)null,
        });
        var noMatch = (await noMatchResp.Content.ReadFromJsonAsync<AutomationDryRunResp>())!;
        noMatch.ConditionsMatched.ShouldBeFalse();
        noMatch.WouldExecute.ShouldBeEmpty();

        // No side effects: the tag from the matched dry-run was never actually created/applied.
        var tags = await client.GetFromJsonAsync<List<TagResp>>("/api/v1/tags");
        tags!.ShouldNotContain(t => t.Name == "dry-run-tag");
    }

    // ---- Business-day date action ----

    [Fact]
    public async Task Business_day_due_date_action_skips_weekends_and_holidays()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Due in 3 business days",
            triggerType = "task.created",
            conditionJson = "{}",
            actionJson = """[{"type":"set_due_date_business_days","value":"{\"days\":\"3\"}"}]""",
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleFullResp>())!;

        var task = await client.CreateTaskAsync(list.Id, "Needs a due date");

        var runs = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<AutomationRunFullResp>>($"/api/v1/automations/{rule.Id}/runs");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(40));
        runs.ShouldNotBeNull();
        runs!.ShouldContain(r => r.Status == "Success" && r.Detail != null && r.Detail.Contains("set_due_date_business_days=ok"));

        // Read the persisted value directly rather than through the HTTP read endpoint: this asserts
        // what the action actually computed and wrote (the thing this test is about), independent of any
        // unrelated read-path latency between the two. Polled: the run showing Success only guarantees
        // the write was issued, not that it's visible to a brand-new connection an instant later.
        DateTimeOffset? dueDate = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (dueDate is null && DateTime.UtcNow < deadline)
        {
            await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT due_date FROM work.tasks WHERE id = @id";
            cmd.Parameters.AddWithValue("id", task.Id);
            // Npgsql returns a "timestamp with time zone" column as a boxed DateTime (Kind=Utc), not a
            // DateTimeOffset -- `value as DateTimeOffset?` silently yields null for that boxed type (the
            // `as` operator on a value type requires an exact runtime-type match), so it never observed
            // an already-correctly-persisted value and this loop spun for the full timeout every time.
            var value = await cmd.ExecuteScalarAsync();
            dueDate = value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                _ => null,
            };
            if (dueDate is null)
            {
                await Task.Delay(500);
            }
        }

        dueDate.ShouldNotBeNull();
        // 3 business days from "now" is always a working day (Mon-Fri default schedule).
        dueDate!.Value.DayOfWeek.ShouldNotBe(DayOfWeek.Saturday);
        dueDate.Value.DayOfWeek.ShouldNotBe(DayOfWeek.Sunday);
    }

    // ---- Helpers: invoke the sweep runners directly (see class doc) ----

    private async Task RunScheduledSweepOnceAsync(Guid workspaceId, Guid ruleId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        BindSystemContext(scope, workspaceId);
        var ruleStore = scope.ServiceProvider.GetRequiredService<IAutomationRuleStore>();
        var rule = await ruleStore.FindAsync(ruleId) ?? throw new InvalidOperationException("Rule not found.");
        var runner = scope.ServiceProvider.GetRequiredService<ScheduledAutomationSweepRunner>();
        await runner.RunForRuleAsync(rule, default);
        await scope.ServiceProvider.GetRequiredService<Planvexa.BuildingBlocks.Domain.IUnitOfWork>().SaveChangesAsync(default);
    }

    private async Task RunDueDateSweepOnceAsync(Guid workspaceId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        BindSystemContext(scope, workspaceId);
        var runner = scope.ServiceProvider.GetRequiredService<DueDateSweepRunner>();
        await runner.RunForWorkspaceAsync(workspaceId, default);
        await scope.ServiceProvider.GetRequiredService<Planvexa.BuildingBlocks.Domain.IUnitOfWork>().SaveChangesAsync(default);
    }

    private async Task RunSlaSweepOnceAsync(Guid workspaceId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        BindSystemContext(scope, workspaceId);
        var runner = scope.ServiceProvider.GetRequiredService<SlaSweepRunner>();
        await runner.RunForWorkspaceAsync(workspaceId, default);
        await scope.ServiceProvider.GetRequiredService<Planvexa.BuildingBlocks.Domain.IUnitOfWork>().SaveChangesAsync(default);
    }

    private async Task RetryOneDueRunAsync(Guid workspaceId, Guid runId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        BindSystemContext(scope, workspaceId);
        var runner = scope.ServiceProvider.GetRequiredService<AutomationRetryRunner>();
        await runner.RetryOneAsync(runId, default);
    }

    /// <summary>Dispatches a synthetic WorkspaceEvent directly via IAutomationDispatcher under a scope
    /// bound to the workspace — same reliable direct-invocation pattern as the sweep helpers above,
    /// used where the test is about action/condition LOGIC rather than the outbox's own delivery timing.</summary>
    private async Task DispatchDirectAsync(Guid workspaceId, string eventType, string entityType, Guid entityId, Guid actorUserId, IReadOnlyDictionary<string, string> data)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        BindSystemContext(scope, workspaceId);
        var dispatcher = scope.ServiceProvider.GetRequiredService<Planvexa.SharedContracts.Automations.IAutomationDispatcher>();
        await dispatcher.DispatchAsync(new WorkspaceEvent(Guid.CreateVersion7(), workspaceId, eventType, entityType, entityId, actorUserId, data), default);
    }

    private void BindSystemContext(IServiceScope scope, Guid workspaceId)
    {
        scope.ServiceProvider.GetRequiredService<IWorkspaceContextAccessor>().Set(new WorkspaceContext(
            workspaceId, PlatformSystemUserId, null, string.Empty, new HashSet<string>(), new HashSet<string>(), "test-automation-sweep"));
        scope.ServiceProvider.GetRequiredService<CurrentUser>().Set(PlatformSystemUserId, "system", "system@planvexa.test", "System");
    }

    /// <summary>Directly inserts a Failed AutomationRun row with its retry already due — the codebase's
    /// action vocabulary is deliberately non-throwing (every action returns false/noop instead), so there
    /// is no reproducible legitimate configuration that makes ExecuteAsync's catch block fire; seeding
    /// the row is the direct way to exercise the retry/dead-letter STATE MACHINE itself (mirrors
    /// GoalsAndReportingFlowTests' BackdateScheduledReportAsync raw-SQL approach).</summary>
    private async Task<Guid> SeedFailedRunAsync(Guid workspaceId, Guid ruleId)
    {
        var runId = Guid.CreateVersion7();
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO automation.automation_runs
                (id, workspace_id, rule_id, event_id, status, detail, occurred_at_utc,
                 event_type, entity_type, entity_id, actor_user_id, data_json, attempts, next_retry_at_utc)
            VALUES
                (@id, @workspaceId, @ruleId, @eventId, 'Failed', 'seeded failure', now(),
                 'task.created', 'Task', @entityId, @actorId, '{}', 1, now() - interval '1 minute')
            """;
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("workspaceId", workspaceId);
        command.Parameters.AddWithValue("ruleId", ruleId);
        command.Parameters.AddWithValue("eventId", Guid.CreateVersion7());
        command.Parameters.AddWithValue("entityId", Guid.CreateVersion7());
        command.Parameters.AddWithValue("actorId", Guid.CreateVersion7());
        await command.ExecuteNonQueryAsync();
        return runId;
    }

    private async Task ForceDeadLetterAsync(Guid runId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE automation.automation_runs SET status = 'DeadLetter', next_retry_at_utc = NULL WHERE id = @id";
        command.Parameters.AddWithValue("id", runId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task BackdateTaskCreationAsync(Guid taskId, TimeSpan age)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE work.tasks SET created_at_utc = @createdAt WHERE id = @id";
        command.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow - age);
        command.Parameters.AddWithValue("id", taskId);
        var affected = await command.ExecuteNonQueryAsync();
        affected.ShouldBe(1, "the task row must exist before backdating it");
    }
}
