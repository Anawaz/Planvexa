namespace Planvexa.Modules.Automations.Application.Services;

using System.Text.Json;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.Modules.Automations.Application;
using Planvexa.Modules.Automations.Domain;
using Planvexa.SharedContracts.Automations;
using Planvexa.SharedContracts.Collaboration;
using Planvexa.SharedContracts.Events;
using Planvexa.SharedContracts.Integrations;
using Planvexa.SharedContracts.Notifications;
using Planvexa.SharedContracts.Reporting;
using Planvexa.SharedContracts.Work;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Executes automations for a <see cref="WorkspaceEvent"/> (implements <see cref="IAutomationDispatcher"/>).
/// Runs under an ambient workspace already set by the host event pipeline (with the system actor, so the
/// task writes it performs do not recursively re-trigger). For each enabled rule matching the trigger it
/// evaluates conditions (flat or nested — see <see cref="AutomationEngine"/>), enforces the workspace
/// monthly run quota, executes actions via cross-module contracts, and records an
/// <see cref="AutomationRun"/> idempotently on (rule, event id). A Failed run is scheduled for retry with
/// backoff rather than left permanently failed — see <see cref="RetryAsync"/>, called by
/// <c>AutomationRetryBackgroundService</c>.
/// </summary>
public sealed class AutomationDispatcher(
    IIdGenerator ids,
    IClock clock,
    IAutomationRuleStore rules,
    IAutomationRunStore runs,
    ITaskWriteApi taskWrite,
    ITaskDirectory taskDirectory,
    INotificationPublisher notifications,
    IEmailSender emailSender,
    IWebhookDispatcher webhookDispatcher,
    IIntegrationActionInvoker integrationActionInvoker,
    ICommentWriteApi commentWriteApi,
    IPlanningQueries planningQueries,
    IWorkspaceAccessQuery access,
    IUnitOfWork unitOfWork) : IAutomationDispatcher
{
    /// <summary>Default per-workspace monthly automation run quota (entitlement override is a later slice).</summary>
    public const int MonthlyRunQuota = 10_000;

    /// <summary>Bounded retry attempts (initial dispatch + this many retries) before a Failed run is
    /// dead-lettered. Not infinite — AGENTS.md rule 13 requires idempotent side effects, not infinite
    /// retries; every action here IS idempotent (SetStatus/AddTag/Assign/etc. are all no-op-if-already-set
    /// or dedup-keyed), so a bounded retry-then-park is safe.</summary>
    public const int MaxRetryAttempts = 5;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task DispatchAsync(WorkspaceEvent workspaceEvent, CancellationToken cancellationToken = default)
    {
        // Loop guard: never react to changes produced by automations/the system itself — see
        // ShouldSkipForRecursionGuard's doc comment for the full argument (including why the new
        // trigger/action combinations, e.g. a scheduled rule whose set_status action raises
        // task.status_changed, still cannot loop).
        if (ShouldSkipForRecursionGuard(workspaceEvent.ActorUserId, workspaceEvent.EventType))
        {
            return;
        }

        var workspaceId = workspaceEvent.WorkspaceId;

        var matching = await rules.ListEnabledByTriggerAsync(workspaceId, workspaceEvent.EventType, cancellationToken);
        if (matching.Count == 0)
        {
            return;
        }

        var monthStart = new DateTimeOffset(clock.UtcNow.Year, clock.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var recorded = false;

        foreach (var rule in matching)
        {
            // Idempotency: a given event triggers a rule at most once.
            if (await runs.ExistsAsync(rule.Id, workspaceEvent.EventId, cancellationToken))
            {
                continue;
            }

            if (!AutomationEngine.Matches(rule.ConditionJson, workspaceEvent))
            {
                continue;
            }

            var usedThisMonth = await runs.CountForWorkspaceSinceAsync(workspaceId, monthStart, cancellationToken);
            if (IsOverQuota(usedThisMonth, MonthlyRunQuota))
            {
                runs.Add(AutomationRun.Record(ids.NewId(), workspaceId, rule.Id, workspaceEvent,
                    AutomationRunStatus.Skipped, "Monthly automation run quota exceeded.", clock.UtcNow));
                recorded = true;
                continue;
            }

            var (status, detail) = await ExecuteAsync(rule, workspaceEvent, cancellationToken);
            var run = AutomationRun.Record(ids.NewId(), workspaceId, rule.Id, workspaceEvent, status, detail, clock.UtcNow);
            if (status == AutomationRunStatus.Failed)
            {
                run.ScheduleFirstRetry(clock.UtcNow);
            }

            runs.Add(run);
            recorded = true;
        }

        if (recorded)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Retries a single Failed <see cref="AutomationRun"/> whose <c>NextRetryAtUtc</c> has
    /// arrived. Called by <c>AutomationRetryBackgroundService</c> under a scope already bound to the run's
    /// workspace (system actor). Re-fetches the current rule (it may have been edited/disabled/deleted
    /// since the original attempt) and re-runs its CURRENT actions against the ORIGINAL triggering event —
    /// re-running the latest action set on retry (rather than a frozen copy) is deliberate: an admin who
    /// fixes a misconfigured action (e.g. corrects a bad field id) wants the pending retry to pick up the
    /// fix, not repeat the same failure. Saves via the caller's unit of work.
    /// </summary>
    public async Task RetryAsync(AutomationRun run, CancellationToken cancellationToken = default)
    {
        var rule = await rules.FindAsync(run.RuleId, cancellationToken);
        if (rule is null || !rule.IsEnabled)
        {
            // Force dead-letter immediately rather than waiting out further backoff for a rule that no
            // longer exists/runs — a future edit/re-enable cannot make this specific stale run retryable.
            // Passing the CURRENT attempt count as maxAttempts guarantees ApplyRetryOutcome's post-
            // increment comparison (Attempts >= maxAttempts) is satisfied on this call.
            run.ApplyRetryOutcome(false, "Rule was deleted or disabled before the retry could run.", run.Attempts, clock.UtcNow);
            return;
        }

        var workspaceEvent = run.ToWorkspaceEvent();
        var (status, detail) = await ExecuteAsync(rule, workspaceEvent, cancellationToken);
        run.ApplyRetryOutcome(status == AutomationRunStatus.Success, detail, MaxRetryAttempts, clock.UtcNow);
    }

    /// <summary>True when the used count has reached or exceeded the quota (pure, unit-tested).</summary>
    public static bool IsOverQuota(int usedThisMonth, int quota) => quota > 0 && usedThisMonth >= quota;

    /// <summary>
    /// Pure recursion-loop guard (unit-tested): true when the event should be ignored because it was
    /// raised by the system itself, UNLESS its type is in <see cref="WorkspaceEvent.Types.SystemActorTriggers"/>
    /// (events legitimately raised by the system with no automation action behind them — anonymous form
    /// submissions, and the due-date/scheduled/SLA background sweeps).
    ///
    /// Specifically re-verified this still holds for every new trigger/action combination: an
    /// automation's action always runs under the system actor (see WorkspaceEventDispatchingPublisher), so
    /// any WorkspaceEvent an action's side effect goes on to raise (e.g. set_status → task.status_changed,
    /// custom_field → a future custom-field-changed event, comment → comment.created) carries the system
    /// actor and is NOT in SystemActorTriggers — so it is unconditionally dropped here, exactly like
    /// before the automations expansion. Only the sweep-synthesized events (task.due_soon/schedule.recurring/
    /// task.sla_breached) and form.submitted are exempt, and no action emits those types, so the
    /// carve-out itself cannot become a new loop.
    /// </summary>
    public static bool ShouldSkipForRecursionGuard(Guid actorUserId, string eventType)
        => actorUserId == PlatformActors.System && !WorkspaceEvent.Types.SystemActorTriggers.Contains(eventType);

    private async Task<(AutomationRunStatus Status, string? Detail)> ExecuteAsync(
        AutomationRule rule, WorkspaceEvent workspaceEvent, CancellationToken ct)
    {
        var actions = AutomationEngine.ParseActions(rule.ActionJson);
        if (actions.Count == 0)
        {
            return (AutomationRunStatus.Success, "No actions.");
        }

        var applied = new List<string>();
        try
        {
            foreach (var action in actions)
            {
                var (ok, note) = await ApplyAsync(action, workspaceEvent, ct);
                applied.Add(note is null ? $"{action.Type}={(ok ? "ok" : "noop")}" : $"{action.Type}={(ok ? "ok" : "noop")} ({note})");
            }

            return (AutomationRunStatus.Success, string.Join(", ", applied));
        }
        catch (Exception ex)
        {
            return (AutomationRunStatus.Failed, $"{string.Join(", ", applied)} | error: {ex.Message}");
        }
    }

    private async Task<(bool Ok, string? Note)> ApplyAsync(AutomationAction action, WorkspaceEvent workspaceEvent, CancellationToken ct)
    {
        var taskId = workspaceEvent.EntityId;
        switch (action.Type)
        {
            case AutomationAction.Types.SetStatus:
                return (await taskWrite.SetStatusByNameAsync(taskId, action.Value, ct), null);

            case AutomationAction.Types.AddTag:
                return (await taskWrite.AddTagByNameAsync(taskId, action.Value, ct), null);

            case AutomationAction.Types.RemoveTag:
                return (await taskWrite.RemoveTagByNameAsync(taskId, action.Value, ct), null);

            case AutomationAction.Types.Assign:
                return (Guid.TryParse(action.Value, out var assignee) && await taskWrite.AssignAsync(taskId, assignee, ct), null);

            case AutomationAction.Types.SetPriority:
                return (await taskWrite.SetPriorityByNameAsync(taskId, action.Value, ct), null);

            case AutomationAction.Types.AssignTeam:
                return (Guid.TryParse(action.Value, out var teamId) && await taskWrite.AssignTeamAsync(taskId, teamId, ct), null);

            case AutomationAction.Types.Notify:
                if (!Guid.TryParse(action.Value, out var recipient))
                {
                    return (false, null);
                }

                await notifications.PublishAsync(new NotificationRequest(
                    RecipientUserId: recipient,
                    EventType: "automation",
                    EntityType: workspaceEvent.EntityType,
                    EntityId: taskId,
                    WorkspaceId: workspaceEvent.WorkspaceId,
                    DeduplicationKey: $"auto:{workspaceEvent.EventId}:{recipient}"), ct);
                return (true, null);

            case AutomationAction.Types.Email:
                return await ApplyEmailAsync(action, workspaceEvent, ct);

            case AutomationAction.Types.Webhook:
                return await ApplyWebhookAsync(action, workspaceEvent, ct);

            case AutomationAction.Types.CustomField:
                return await ApplyCustomFieldAsync(action, taskId, ct);

            case AutomationAction.Types.Comment:
                return await ApplyCommentAsync(action, workspaceEvent, taskId, ct);

            case AutomationAction.Types.SetDueDateBusinessDays:
                return await ApplySetDueDateBusinessDaysAsync(action, workspaceEvent.WorkspaceId, taskId, ct);

            case AutomationAction.Types.Integration:
                return await ApplyIntegrationAsync(action, workspaceEvent, taskId, ct);

            default:
                return (false, null);
        }
    }

    /// <summary>
    /// Security (see roadmap's CRITICAL SECURITY CONTEXT): the recipient is restricted to a CURRENT
    /// workspace member — validated here even though the automation itself runs as the system actor —
    /// so an email action cannot be used to exfiltrate task details to a user id that has since left (or
    /// never belonged to) the workspace. Value is JSON: {"recipientUserId":"...","subject":"...","body":"..."}.
    /// </summary>
    private async Task<(bool, string?)> ApplyEmailAsync(AutomationAction action, WorkspaceEvent workspaceEvent, CancellationToken ct)
    {
        EmailActionConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<EmailActionConfig>(action.Value, JsonOptions);
        }
        catch (JsonException)
        {
            return (false, "invalid email config");
        }

        if (config is null || !Guid.TryParse(config.RecipientUserId, out var recipientUserId))
        {
            return (false, "missing recipientUserId");
        }

        var membership = await access.GetAccessAsync(workspaceEvent.WorkspaceId, recipientUserId, ct);
        if (membership is null)
        {
            return (false, "recipient is not a workspace member");
        }

        var taskTitle = await ResolveTaskTitleAsync(workspaceEvent.EntityId, ct);
        var subject = Interpolate(config.Subject ?? string.Empty, workspaceEvent, taskTitle);
        var body = Interpolate(config.Body ?? string.Empty, workspaceEvent, taskTitle);
        await emailSender.SendAsync(recipientUserId, subject, body, ct);
        return (true, null);
    }

    /// <summary>Value is JSON: {"url":"https://..."}. Reuses the Integrations module's signed ad-hoc
    /// webhook pipeline (see IWebhookDispatcher.SendAdHocAsync) rather than issuing a raw HTTP call.</summary>
    private async Task<(bool, string?)> ApplyWebhookAsync(AutomationAction action, WorkspaceEvent workspaceEvent, CancellationToken ct)
    {
        WebhookActionConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<WebhookActionConfig>(action.Value, JsonOptions);
        }
        catch (JsonException)
        {
            return (false, "invalid webhook config");
        }

        if (config is null || string.IsNullOrWhiteSpace(config.Url))
        {
            return (false, "missing url");
        }

        var payload = JsonSerializer.Serialize(new
        {
            eventId = workspaceEvent.EventId,
            eventType = workspaceEvent.EventType,
            workspaceId = workspaceEvent.WorkspaceId,
            entityType = workspaceEvent.EntityType,
            entityId = workspaceEvent.EntityId,
            data = workspaceEvent.Data,
        }, JsonOptions);

        var ok = await webhookDispatcher.SendAdHocAsync(workspaceEvent.WorkspaceId, config.Url, payload, ct);
        return (ok, null);
    }

    /// <summary>Value is JSON: {"provider":"slack"|"github","message":"...","issueNumber":"..."} (see
    /// AutomationAction.Types.Integration doc comment). Routes to the Integrations module's real client for
    /// the requested provider via <see cref="IIntegrationActionInvoker"/>; unconfigured/unsupported
    /// providers report a clear failure detail rather than a faked success.</summary>
    private async Task<(bool, string?)> ApplyIntegrationAsync(AutomationAction action, WorkspaceEvent workspaceEvent, Guid taskId, CancellationToken ct)
    {
        IntegrationActionConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<IntegrationActionConfig>(action.Value, JsonOptions);
        }
        catch (JsonException)
        {
            return (false, "invalid integration config");
        }

        if (config is null || string.IsNullOrWhiteSpace(config.Provider))
        {
            return (false, "missing provider");
        }

        int? issueNumber = null;
        if (!string.IsNullOrWhiteSpace(config.IssueNumber))
        {
            if (!int.TryParse(config.IssueNumber, out var parsedIssueNumber))
            {
                return (false, "invalid issueNumber");
            }

            issueNumber = parsedIssueNumber;
        }

        var taskTitle = await ResolveTaskTitleAsync(taskId, ct);
        var message = Interpolate(config.Message ?? string.Empty, workspaceEvent, taskTitle);
        var result = await integrationActionInvoker.InvokeAsync(workspaceEvent.WorkspaceId, config.Provider, message, issueNumber, ct);
        return (result.Success, result.Detail);
    }

    /// <summary>Value is JSON: {"fieldId":"<definition guid>","value":"..."}.</summary>
    private async Task<(bool, string?)> ApplyCustomFieldAsync(AutomationAction action, Guid taskId, CancellationToken ct)
    {
        CustomFieldActionConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<CustomFieldActionConfig>(action.Value, JsonOptions);
        }
        catch (JsonException)
        {
            return (false, "invalid custom_field config");
        }

        if (config is null || !Guid.TryParse(config.FieldId, out var fieldId))
        {
            return (false, "missing fieldId");
        }

        return (await taskWrite.SetCustomFieldValueAsync(taskId, fieldId, config.Value, ct), null);
    }

    /// <summary>Comment is posted as the system actor (not impersonating any workspace member) — see
    /// ICommentWriteApi's doc comment. Value is the (interpolated) comment body text.</summary>
    private async Task<(bool, string?)> ApplyCommentAsync(AutomationAction action, WorkspaceEvent workspaceEvent, Guid taskId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(action.Value))
        {
            return (false, null);
        }

        var taskTitle = await ResolveTaskTitleAsync(taskId, ct);
        var body = Interpolate(action.Value, workspaceEvent, taskTitle);
        var commentId = await commentWriteApi.PostSystemCommentAsync(workspaceEvent.WorkspaceId, taskId, PlatformActors.System, body, ct);
        return (commentId is not null, null);
    }

    /// <summary>Value is JSON: {"days":"3"}. Uses IPlanningQueries.AddBusinessDaysAsync (the
    /// business-day helper, backed by the Planning module's WorkSchedule/Holiday calendar).</summary>
    private async Task<(bool, string?)> ApplySetDueDateBusinessDaysAsync(AutomationAction action, Guid workspaceId, Guid taskId, CancellationToken ct)
    {
        DueDateActionConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<DueDateActionConfig>(action.Value, JsonOptions);
        }
        catch (JsonException)
        {
            return (false, "invalid due-date config");
        }

        if (config is null || !int.TryParse(config.Days, out var days))
        {
            return (false, "missing days");
        }

        var dueDate = await planningQueries.AddBusinessDaysAsync(workspaceId, clock.UtcNow, days, ct);
        return (await taskWrite.SetDueDateAsync(taskId, dueDate, ct), null);
    }

    private async Task<string?> ResolveTaskTitleAsync(Guid taskId, CancellationToken ct)
        => (await taskDirectory.FindAsync(taskId, ct))?.Title;

    /// <summary>Minimal token interpolation for email/comment action templates: {{task.title}},
    /// {{task.id}}, {{event.type}}, and {{data.KEY}} for any key in the event's Data dictionary. Pure
    /// string substitution — no expression evaluation, per AGENTS.md rule 16 (prefer the simplest thing
    /// that works over a templating dependency).</summary>
    private static string Interpolate(string template, WorkspaceEvent workspaceEvent, string? taskTitle)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var result = template
            .Replace("{{task.title}}", taskTitle ?? string.Empty, StringComparison.Ordinal)
            .Replace("{{task.id}}", workspaceEvent.EntityId.ToString(), StringComparison.Ordinal)
            .Replace("{{event.type}}", workspaceEvent.EventType, StringComparison.Ordinal);

        foreach (var (key, value) in workspaceEvent.Data)
        {
            result = result.Replace($"{{{{data.{key}}}}}", value, StringComparison.Ordinal);
        }

        return result;
    }

    private sealed record EmailActionConfig(string? RecipientUserId, string? Subject, string? Body);
    private sealed record WebhookActionConfig(string? Url);
    private sealed record IntegrationActionConfig(string? Provider, string? Message, string? IssueNumber);
    private sealed record CustomFieldActionConfig(string? FieldId, string? Value);
    private sealed record DueDateActionConfig(string? Days);
}
