namespace Planvexa.UnitTests.Automations;

using Planvexa.Modules.Automations.Application.Services;
using Planvexa.Modules.Automations.Authorization;
using Planvexa.Modules.Automations.Domain;
using Planvexa.SharedContracts.Events;
using Planvexa.SharedContracts.Workspaces;
using Shouldly;
using Xunit;

public sealed class AutomationEngineTests
{
    private static WorkspaceEvent Event(string type, params (string Key, string Value)[] data)
        => new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), type, "Task", Guid.CreateVersion7(),
            Guid.CreateVersion7(), data.ToDictionary(d => d.Key, d => d.Value));

    [Fact]
    public void Empty_conditions_always_match()
        => AutomationEngine.Matches("{}", Event(WorkspaceEvent.Types.TaskCompleted)).ShouldBeTrue();

    [Fact]
    public void Conditions_match_when_every_key_equals_event_data()
    {
        var e = Event(WorkspaceEvent.Types.TaskStatusChanged, ("toStatusId", "abc"));
        AutomationEngine.Matches("{\"toStatusId\":\"abc\"}", e).ShouldBeTrue();
    }

    [Fact]
    public void Conditions_fail_on_mismatch_or_missing_key()
    {
        var e = Event(WorkspaceEvent.Types.TaskStatusChanged, ("toStatusId", "abc"));
        AutomationEngine.Matches("{\"toStatusId\":\"xyz\"}", e).ShouldBeFalse();
        AutomationEngine.Matches("{\"missing\":\"1\"}", e).ShouldBeFalse();
    }

    [Fact]
    public void Condition_matching_is_case_insensitive_on_value()
    {
        var e = Event(WorkspaceEvent.Types.TaskCompleted, ("k", "Done"));
        AutomationEngine.Matches("{\"k\":\"done\"}", e).ShouldBeTrue();
    }

    [Fact]
    public void ParseActions_keeps_known_types_and_drops_unknown()
    {
        var json = "[{\"type\":\"set_status\",\"value\":\"Complete\"},{\"type\":\"bogus\",\"value\":\"x\"},{\"type\":\"assign\",\"value\":\"u\"}]";
        var actions = AutomationEngine.ParseActions(json);
        actions.Count.ShouldBe(2);
        actions.Select(a => a.Type).ShouldBe(new[] { "set_status", "assign" });
    }

    [Fact]
    public void ParseActions_tolerates_garbage()
        => AutomationEngine.ParseActions("not json").ShouldBeEmpty();

    [Theory]
    [InlineData(0, 10, false)]
    [InlineData(9, 10, false)]
    [InlineData(10, 10, true)]
    [InlineData(11, 10, true)]
    [InlineData(5, 0, false)]
    public void Quota_gate_triggers_at_or_above_limit(int used, int quota, bool over)
        => AutomationDispatcher.IsOverQuota(used, quota).ShouldBe(over);

    // ---- nested AND/OR condition groups ----

    [Fact]
    public void Nested_and_group_requires_every_child_to_match()
    {
        var data = new Dictionary<string, string> { ["toStatusId"] = "abc", ["priority"] = "High" };
        var json = """{"and":[{"field":"toStatusId","equals":"abc"},{"field":"priority","equals":"High"}]}""";
        AutomationEngine.Matches(json, data).ShouldBeTrue();

        var mismatch = """{"and":[{"field":"toStatusId","equals":"abc"},{"field":"priority","equals":"Low"}]}""";
        AutomationEngine.Matches(mismatch, data).ShouldBeFalse();
    }

    [Fact]
    public void Nested_or_group_requires_at_least_one_child_to_match()
    {
        var data = new Dictionary<string, string> { ["priority"] = "Low" };
        var json = """{"or":[{"field":"priority","equals":"High"},{"field":"priority","equals":"Low"}]}""";
        AutomationEngine.Matches(json, data).ShouldBeTrue();

        var noMatch = """{"or":[{"field":"priority","equals":"High"},{"field":"priority","equals":"Urgent"}]}""";
        AutomationEngine.Matches(noMatch, data).ShouldBeFalse();
    }

    [Fact]
    public void Nested_groups_can_nest_arbitrarily_deep()
    {
        // (priority = High) OR (statusId = abc AND minutesInStatus >= 120)
        var json = """
        {"or":[
            {"field":"priority","equals":"High"},
            {"and":[{"field":"statusId","equals":"abc"},{"field":"minutesInStatus","gte":"120"}]}
        ]}
        """;

        AutomationEngine.Matches(json, new Dictionary<string, string> { ["priority"] = "High" }).ShouldBeTrue();
        AutomationEngine.Matches(json, new Dictionary<string, string> { ["statusId"] = "abc", ["minutesInStatus"] = "180" }).ShouldBeTrue();
        AutomationEngine.Matches(json, new Dictionary<string, string> { ["statusId"] = "abc", ["minutesInStatus"] = "60" }).ShouldBeFalse();
        AutomationEngine.Matches(json, new Dictionary<string, string> { ["priority"] = "Low" }).ShouldBeFalse();
    }

    [Theory]
    [InlineData("119", false)]
    [InlineData("120", true)]
    [InlineData("121", true)]
    public void Gte_leaf_is_a_numeric_threshold_not_a_string_comparison(string actual, bool expected)
        => AutomationEngine.Matches(
            """{"field":"minutesInStatus","gte":"120"}""",
            new Dictionary<string, string> { ["minutesInStatus"] = actual }).ShouldBe(expected);

    [Theory]
    [InlineData("61", false)]
    [InlineData("60", true)]
    [InlineData("10", true)]
    public void Lte_leaf_is_a_numeric_threshold(string actual, bool expected)
        => AutomationEngine.Matches(
            """{"field":"daysUntilDue","lte":"60"}""",
            new Dictionary<string, string> { ["daysUntilDue"] = actual }).ShouldBe(expected);

    [Fact]
    public void Existing_flat_legacy_rules_keep_evaluating_exactly_as_before()
    {
        // No "and"/"or"/"field" key at the root -> treated as the ORIGINAL flat key=value AND semantics,
        // proving the tree format is purely additive.
        var e = Event(WorkspaceEvent.Types.TaskStatusChanged, ("toStatusId", "abc"), ("listId", "L1"));
        AutomationEngine.Matches("""{"toStatusId":"abc","listId":"L1"}""", e).ShouldBeTrue();
        AutomationEngine.Matches("""{"toStatusId":"abc","listId":"L2"}""", e).ShouldBeFalse();
    }

    // ---- dry-run is a thin wrapper over Matches + ParseActions with no side effects ----
    // (AutomationRuleService.DryRunAsync calls exactly these two pure functions and never touches a
    // cross-module write API — see its implementation.)

    [Fact]
    public void DryRun_style_evaluation_reports_matched_conditions_and_predicted_actions()
    {
        var conditionJson = """{"field":"toStatusId","equals":"blocked-status-id"}""";
        var actionJson = """[{"type":"add_tag","value":"blocked"},{"type":"notify","value":""}]""";

        var matchingData = new Dictionary<string, string> { ["toStatusId"] = "blocked-status-id" };
        AutomationEngine.Matches(conditionJson, matchingData).ShouldBeTrue();
        var predicted = AutomationEngine.ParseActions(actionJson);
        predicted.Select(a => a.Type).ShouldBe(new[] { "add_tag", "notify" });

        var nonMatchingData = new Dictionary<string, string> { ["toStatusId"] = "other-status-id" };
        AutomationEngine.Matches(conditionJson, nonMatchingData).ShouldBeFalse();
    }

    // ---- new action types are recognized and structured JSON values pass through untouched ----

    [Fact]
    public void ParseActions_recognizes_every_scheduled_action_type()
    {
        var json = """
        [
            {"type":"email","value":"{\"recipientUserId\":\"u\"}"},
            {"type":"webhook","value":"{\"url\":\"https://x\"}"},
            {"type":"custom_field","value":"{\"fieldId\":\"f\"}"},
            {"type":"comment","value":"hi"},
            {"type":"set_due_date_business_days","value":"{\"days\":\"3\"}"},
            {"type":"integration","value":"{}"}
        ]
        """;
        var actions = AutomationEngine.ParseActions(json);
        actions.Count.ShouldBe(6);
        actions.Select(a => a.Type).ShouldBe(new[]
        {
            "email", "webhook", "custom_field", "comment", "set_due_date_business_days", "integration",
        });
    }

    // ---- recursion protection covers the new trigger/action combinations ----

    [Fact]
    public void Recursion_guard_drops_system_actor_events_of_ordinary_trigger_types()
    {
        // The core loop scenario item 10 asks for: an automation's action (e.g. set_status) runs under
        // the system actor and its side effect raises an ordinary event type (task.status_changed) —
        // exactly what would happen if a SCHEDULED rule (the new trigger type) had a set_status
        // action, and another rule reacted to task.status_changed. That must be dropped, or the two rules
        // would fire each other forever.
        AutomationDispatcher.ShouldSkipForRecursionGuard(Planvexa.BuildingBlocks.Platform.PlatformActors.System, WorkspaceEvent.Types.TaskStatusChanged)
            .ShouldBeTrue();

        // Same for every other ordinary trigger type an action could indirectly cause.
        AutomationDispatcher.ShouldSkipForRecursionGuard(Planvexa.BuildingBlocks.Platform.PlatformActors.System, WorkspaceEvent.Types.CommentCreated)
            .ShouldBeTrue();
        AutomationDispatcher.ShouldSkipForRecursionGuard(Planvexa.BuildingBlocks.Platform.PlatformActors.System, WorkspaceEvent.Types.TaskAssigned)
            .ShouldBeTrue();
    }

    [Fact]
    public void Recursion_guard_allows_the_system_actor_for_sweep_synthesized_trigger_types()
    {
        // These are never raised by an automation action's side effect (only by the background sweeps),
        // so letting them through cannot create a loop — see WorkspaceEvent.Types.SystemActorTriggers.
        foreach (var triggerType in WorkspaceEvent.Types.SystemActorTriggers)
        {
            AutomationDispatcher.ShouldSkipForRecursionGuard(Planvexa.BuildingBlocks.Platform.PlatformActors.System, triggerType)
                .ShouldBeFalse();
        }
    }

    [Fact]
    public void Recursion_guard_never_drops_a_real_users_event()
        => AutomationDispatcher.ShouldSkipForRecursionGuard(Guid.CreateVersion7(), WorkspaceEvent.Types.TaskStatusChanged).ShouldBeFalse();
}

public sealed class AutomationRuleDomainTests
{
    [Fact]
    public void Create_rejects_unknown_trigger()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            AutomationRule.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "r", "task.bogus", null, null, Guid.CreateVersion7(), DateTimeOffset.UtcNow));

    [Fact]
    public void Enable_disable_toggles_state()
    {
        var rule = AutomationRule.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "r", WorkspaceEvent.Types.TaskCreated, null, null, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        rule.IsEnabled.ShouldBeTrue();
        rule.Disable(DateTimeOffset.UtcNow);
        rule.IsEnabled.ShouldBeFalse();
        rule.Enable(DateTimeOffset.UtcNow);
        rule.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Update_increments_version_and_snapshot_captures_the_pre_change_state()
    {
        var rule = AutomationRule.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "original", WorkspaceEvent.Types.TaskCreated, "{}", "[]", Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        rule.Version.ShouldBe(1);

        var snapshotBeforeUpdate = rule.SnapshotForVersioning();
        snapshotBeforeUpdate.Name.ShouldBe("original");
        snapshotBeforeUpdate.Version.ShouldBe(1);

        rule.Update("renamed", null, null, null, DateTimeOffset.UtcNow);
        rule.Version.ShouldBe(2);
        rule.Name.ShouldBe("renamed");

        // The snapshot taken BEFORE the update still reflects the OLD name — proving a version row built
        // from it (AutomationRuleVersion.Capture, called before Update in AutomationRuleService) preserves
        // history rather than the post-update state.
        snapshotBeforeUpdate.Name.ShouldBe("original");
    }

    [Fact]
    public void RestoreFrom_reapplies_a_captured_versions_fields_and_itself_counts_as_a_new_version()
    {
        var rule = AutomationRule.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "v1", WorkspaceEvent.Types.TaskCreated, "{}", "[]", Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        var version1Snapshot = AutomationRuleVersion.Capture(Guid.CreateVersion7(), rule.WorkspaceId, rule.Id, rule, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        rule.Update("v2", null, null, null, DateTimeOffset.UtcNow);
        rule.Name.ShouldBe("v2");
        rule.Version.ShouldBe(2);

        rule.RestoreFrom(version1Snapshot, DateTimeOffset.UtcNow);
        rule.Name.ShouldBe("v1");
        rule.Version.ShouldBe(3); // the revert is itself a new version, not a rewind of the counter.
    }
}

public sealed class AutomationRunRetryDomainTests
{
    private static AutomationRun MakeRun(AutomationRunStatus status = AutomationRunStatus.Failed)
    {
        var evt = new WorkspaceEvent(
            Guid.CreateVersion7(), Guid.CreateVersion7(), WorkspaceEvent.Types.TaskCreated, "Task",
            Guid.CreateVersion7(), Guid.CreateVersion7(), new Dictionary<string, string>());
        return AutomationRun.Record(Guid.CreateVersion7(), evt.WorkspaceId, Guid.CreateVersion7(), evt, status, "boom", DateTimeOffset.UtcNow);
    }

    [Fact]
    public void A_failed_run_starts_at_attempt_one_with_no_retry_scheduled_until_ScheduleFirstRetry()
    {
        var run = MakeRun();
        run.Attempts.ShouldBe(1);
        run.NextRetryAtUtc.ShouldBeNull();

        var now = DateTimeOffset.UtcNow;
        run.ScheduleFirstRetry(now);
        run.NextRetryAtUtc.ShouldNotBeNull();
        run.NextRetryAtUtc!.Value.ShouldBeGreaterThan(now);
    }

    [Fact]
    public void ApplyRetryOutcome_success_clears_retry_state_and_marks_success()
    {
        var run = MakeRun();
        run.ScheduleFirstRetry(DateTimeOffset.UtcNow);

        run.ApplyRetryOutcome(success: true, detail: "ok", maxAttempts: 5, nowUtc: DateTimeOffset.UtcNow);

        run.Status.ShouldBe(AutomationRunStatus.Success);
        run.NextRetryAtUtc.ShouldBeNull();
        run.Attempts.ShouldBe(2);
    }

    [Fact]
    public void ApplyRetryOutcome_failure_before_max_attempts_reschedules_with_backoff()
    {
        var run = MakeRun();
        var now = DateTimeOffset.UtcNow;

        run.ApplyRetryOutcome(success: false, detail: "still broken", maxAttempts: 5, nowUtc: now);

        run.Status.ShouldBe(AutomationRunStatus.Failed);
        run.Attempts.ShouldBe(2);
        run.NextRetryAtUtc.ShouldNotBeNull();
        run.NextRetryAtUtc!.Value.ShouldBeGreaterThan(now);
    }

    [Fact]
    public void ApplyRetryOutcome_failure_at_max_attempts_dead_letters_the_run()
    {
        var run = MakeRun();
        var now = DateTimeOffset.UtcNow;

        // maxAttempts = 5: attempts 1 (initial) through 4 retries should stay Failed; the 5th marks DeadLetter.
        for (var i = 0; i < 3; i++)
        {
            run.ApplyRetryOutcome(success: false, detail: "still broken", maxAttempts: 5, nowUtc: now);
            run.Status.ShouldBe(AutomationRunStatus.Failed);
        }

        run.Attempts.ShouldBe(4);
        run.ApplyRetryOutcome(success: false, detail: "still broken", maxAttempts: 5, nowUtc: now);
        run.Attempts.ShouldBe(5);
        run.Status.ShouldBe(AutomationRunStatus.DeadLetter);
        run.NextRetryAtUtc.ShouldBeNull(); // terminal: the sweep must not keep picking this run up.
    }

    [Fact]
    public void Retries_are_bounded_not_infinite()
    {
        // AGENTS.md rule 13 (idempotent side effects) pairs with a BOUNDED retry count, not infinite
        // retries — this asserts a run that keeps failing reaches DeadLetter at exactly
        // AutomationDispatcher.MaxRetryAttempts and never exceeds it. (The production retry sweep only
        // re-queries runs still in the Failed status, so a DeadLetter run is never handed to
        // ApplyRetryOutcome again — this loop stops there too, matching real usage.)
        var run = MakeRun();
        var iterations = 0;
        while (run.Status != AutomationRunStatus.DeadLetter && iterations < 100)
        {
            run.ApplyRetryOutcome(success: false, detail: "still broken", maxAttempts: AutomationDispatcher.MaxRetryAttempts, nowUtc: DateTimeOffset.UtcNow);
            iterations++;
        }

        run.Status.ShouldBe(AutomationRunStatus.DeadLetter);
        run.Attempts.ShouldBe(AutomationDispatcher.MaxRetryAttempts);
    }

    [Fact]
    public void RearmForManualRetry_reactivates_a_dead_lettered_run_for_one_more_attempt()
    {
        var run = MakeRun(AutomationRunStatus.DeadLetter);
        var now = DateTimeOffset.UtcNow;

        run.RearmForManualRetry(now);

        run.Status.ShouldBe(AutomationRunStatus.Failed);
        run.NextRetryAtUtc.ShouldBe(now);
    }

    [Fact]
    public void ToWorkspaceEvent_reconstructs_the_original_triggering_event_for_a_retry()
    {
        var eventId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        var taskId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var evt = new WorkspaceEvent(eventId, workspaceId, WorkspaceEvent.Types.TaskStatusChanged, "Task", taskId, actorId,
            new Dictionary<string, string> { ["toStatusId"] = "abc" });

        var run = AutomationRun.Record(Guid.CreateVersion7(), workspaceId, Guid.CreateVersion7(), evt, AutomationRunStatus.Failed, "boom", DateTimeOffset.UtcNow);

        var reconstructed = run.ToWorkspaceEvent();
        reconstructed.EventId.ShouldBe(eventId);
        reconstructed.WorkspaceId.ShouldBe(workspaceId);
        reconstructed.EventType.ShouldBe(WorkspaceEvent.Types.TaskStatusChanged);
        reconstructed.EntityId.ShouldBe(taskId);
        reconstructed.ActorUserId.ShouldBe(actorId);
        reconstructed.Data["toStatusId"].ShouldBe("abc");
    }
}

public sealed class AutomationsAuthorizerTests
{
    [Theory]
    [InlineData(WorkspaceRole.Guest, false)]
    [InlineData(WorkspaceRole.Member, false)]
    [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Owner, true)]
    public void Manage_requires_admin(WorkspaceRole role, bool allowed)
        => AutomationsAuthorizer.CanManage(role).ShouldBe(allowed);
}
