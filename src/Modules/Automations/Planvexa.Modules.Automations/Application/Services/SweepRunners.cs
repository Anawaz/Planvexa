namespace Planvexa.Modules.Automations.Application.Services;

using System.Text.Json;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.Modules.Automations.Application;
using Planvexa.Modules.Automations.Domain;
using Planvexa.SharedContracts.Automations;
using Planvexa.SharedContracts.Events;
using Planvexa.SharedContracts.Work;

/// <summary>
/// The sweep logic for the "due-date"/"scheduled"/"SLA" automation triggers — none of these
/// is a discrete event that fires on its own, so a periodic background scan is needed (see
/// DueDateBackgroundService/ScheduledAutomationBackgroundService/SlaBackgroundService in the composition
/// root, which mirror ScheduledReportBackgroundService's "list candidates across workspaces, then run one
/// under a scope bound to its own workspace" shape). Each sweep's only job is deciding WHICH synthetic
/// <see cref="WorkspaceEvent"/>s should exist right now — the events are then fed through the ORDINARY
/// <see cref="IAutomationDispatcher.DispatchAsync"/> pipeline, so condition matching, the monthly quota,
/// idempotent run recording, and retry scheduling are all the exact same code path as every other
/// trigger type. Idempotency (never firing the same logical occurrence twice) comes from a deterministic
/// <see cref="DeterministicGuid"/>-derived event id, not from any separate "already ran" bookkeeping —
/// AutomationDispatcher already de-dupes on (rule, event id).
/// </summary>
public sealed class DueDateSweepRunner(
    IAutomationRuleStore rules,
    ITaskDirectory tasks,
    IAutomationDispatcher dispatcher,
    IClock clock)
{
    // ponytail: a fixed scan window (catch a task that JUST passed due within the last day, plus the
    // next two weeks) rather than a per-rule configurable lookahead — a rule that only cares about a
    // narrower/wider band already can (and should) express that with a nested condition on
    // "daysUntilDue" (e.g. {"field":"daysUntilDue","lte":"1"}), so no rule can miss a task inside this
    // window. Widen if a real workspace needs a due-date reminder further out than 14 days.
    private static readonly TimeSpan LookBack = TimeSpan.FromDays(1);
    private static readonly TimeSpan LookAhead = TimeSpan.FromDays(14);

    public async Task<IReadOnlyList<Guid>> ListCandidateWorkspaceIdsAsync(CancellationToken ct)
        => (await rules.ListEnabledByTriggerAcrossWorkspacesAsync(WorkspaceEvent.Types.TaskDueSoon, ct))
            .Select(r => r.WorkspaceId).Distinct().ToList();

    /// <summary>Must be called under a scope whose ambient workspace is already bound to
    /// <paramref name="workspaceId"/> (same requirement as IAutomationDispatcher itself).</summary>
    public async Task RunForWorkspaceAsync(Guid workspaceId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await tasks.ListDueBetweenAsync(workspaceId, now - LookBack, now + LookAhead, ct);
        foreach (var task in due)
        {
            var daysUntilDue = (int)Math.Round((task.DueDate - now).TotalDays, MidpointRounding.AwayFromZero);
            // Bucketed by calendar day: fires at most once per day per task while it stays in the window.
            var eventId = DeterministicGuid.From($"due:{task.TaskId}:{now:yyyy-MM-dd}");
            var workspaceEvent = new WorkspaceEvent(
                eventId, workspaceId, WorkspaceEvent.Types.TaskDueSoon, "Task", task.TaskId, PlatformActors.System,
                new Dictionary<string, string> { ["daysUntilDue"] = daysUntilDue.ToString() });
            await dispatcher.DispatchAsync(workspaceEvent, ct);
        }
    }
}

/// <summary>See <see cref="DueDateSweepRunner"/>'s class doc for the shared sweep design.</summary>
public sealed class ScheduledAutomationSweepRunner(
    IAutomationRuleStore rules,
    IAutomationDispatcher dispatcher,
    IClock clock)
{
    public Task<IReadOnlyList<AutomationRule>> ListDueRulesAsync(CancellationToken ct)
        => rules.ListEnabledByTriggerAcrossWorkspacesAsync(WorkspaceEvent.Types.ScheduleRecurring, ct);

    /// <summary>Must be called under a scope whose ambient workspace is already bound to the rule's own
    /// workspace. Value is JSON: <c>{"everyMinutes":60}</c> (in <see cref="AutomationRule.TriggerConfigJson"/>);
    /// a missing/invalid/non-positive interval means the rule never fires.</summary>
    public async Task RunForRuleAsync(AutomationRule rule, CancellationToken ct)
    {
        var everyMinutes = ParseEveryMinutes(rule.TriggerConfigJson);
        if (everyMinutes <= 0)
        {
            return;
        }

        var now = clock.UtcNow;
        // Bucketed by (rule, time-slot of width everyMinutes): fires at most once per interval.
        var slot = now.Ticks / TimeSpan.FromMinutes(everyMinutes).Ticks;
        var eventId = DeterministicGuid.From($"schedule:{rule.Id}:{slot}");
        var workspaceEvent = new WorkspaceEvent(
            eventId, rule.WorkspaceId, WorkspaceEvent.Types.ScheduleRecurring, "AutomationRule", rule.Id, PlatformActors.System,
            new Dictionary<string, string>());
        await dispatcher.DispatchAsync(workspaceEvent, ct);
    }

    private static int ParseEveryMinutes(string? triggerConfigJson)
    {
        if (string.IsNullOrWhiteSpace(triggerConfigJson))
        {
            return 0;
        }

        try
        {
            using var doc = JsonDocument.Parse(triggerConfigJson);
            return doc.RootElement.TryGetProperty("everyMinutes", out var el) && el.TryGetInt32(out var minutes) ? minutes : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}

/// <summary>See <see cref="DueDateSweepRunner"/>'s class doc for the shared sweep design.</summary>
public sealed class SlaSweepRunner(
    IAutomationRuleStore rules,
    ITaskDirectory tasks,
    IAutomationDispatcher dispatcher,
    IClock clock)
{
    public async Task<IReadOnlyList<Guid>> ListCandidateWorkspaceIdsAsync(CancellationToken ct)
        => (await rules.ListEnabledByTriggerAcrossWorkspacesAsync(WorkspaceEvent.Types.TaskSlaBreached, ct))
            .Select(r => r.WorkspaceId).Distinct().ToList();

    /// <summary>Must be called under a scope whose ambient workspace is already bound to
    /// <paramref name="workspaceId"/>. Emits one candidate event PER open task (not just ones that already
    /// breach some threshold) — the rule's own condition (e.g. {"field":"minutesInStatus","gte":"2880"})
    /// decides whether it actually matches; this sweep only supplies the "minutes in current status" fact.</summary>
    public async Task RunForWorkspaceAsync(Guid workspaceId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var ages = await tasks.ListOpenTaskStatusAgesAsync(workspaceId, ct);
        foreach (var age in ages)
        {
            var minutesInStatus = (int)(now - age.EnteredStatusAtUtc).TotalMinutes;
            if (minutesInStatus <= 0)
            {
                continue;
            }

            // Bucketed by calendar day: fires at most once per day per task while the breach persists.
            var eventId = DeterministicGuid.From($"sla:{age.TaskId}:{now:yyyy-MM-dd}");
            var workspaceEvent = new WorkspaceEvent(
                eventId, workspaceId, WorkspaceEvent.Types.TaskSlaBreached, "Task", age.TaskId, PlatformActors.System,
                new Dictionary<string, string>
                {
                    ["statusId"] = age.StatusId.ToString(),
                    ["minutesInStatus"] = minutesInStatus.ToString(),
                });
            await dispatcher.DispatchAsync(workspaceEvent, ct);
        }
    }
}

/// <summary>
/// Bounded retry-with-backoff for Failed automation runs (see <see cref="AutomationRun"/>'s
/// Attempts/NextRetryAtUtc). Mirrors the same "list candidates across workspaces, run one under a scope
/// bound to its own workspace" split as the trigger sweeps above, driven by
/// AutomationRetryBackgroundService.
/// </summary>
public sealed class AutomationRetryRunner(
    IAutomationRunStore runs,
    AutomationDispatcher dispatcher,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private const int BatchSize = 50;

    public async Task<IReadOnlyList<(Guid RunId, Guid WorkspaceId)>> ListDueForRetryAsync(CancellationToken ct)
        => (await runs.ListDueForRetryAsync(clock.UtcNow, BatchSize, ct))
            .Select(r => (r.Id, r.WorkspaceId))
            .ToList();

    /// <summary>Must be called under a scope whose ambient workspace is already bound to the run's own
    /// workspace.</summary>
    public async Task RetryOneAsync(Guid runId, CancellationToken ct)
    {
        var run = await runs.FindAsync(runId, ct);
        if (run is null)
        {
            return;
        }

        await dispatcher.RetryAsync(run, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
