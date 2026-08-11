namespace Planvexa.Modules.Automations.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Events;

/// <summary>
/// A workspace automation: when an event of <see cref="TriggerType"/> occurs and the (JSON) conditions
/// match, the (JSON) actions are executed. Rules are workspace-owned and only run while enabled.
/// </summary>
public sealed class AutomationRule : Entity, IAggregateRoot, IWorkspaceOwned
{
    private AutomationRule()
    {
    }

    private AutomationRule(
        Guid id, Guid workspaceId, string name, string triggerType,
        string conditionJson, string actionJson, string? triggerConfigJson, Guid createdBy, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        TriggerType = triggerType;
        ConditionJson = conditionJson;
        ActionJson = actionJson;
        TriggerConfigJson = triggerConfigJson;
        IsEnabled = true;
        Version = 1;
        CreatedByUserId = createdBy;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TriggerType { get; private set; } = string.Empty;
    public string ConditionJson { get; private set; } = "{}";
    public string ActionJson { get; private set; } = "[]";

    /// <summary>Trigger-specific configuration that isn't a condition on the triggering event's
    /// data — e.g. a "scheduled" rule's recurrence (<c>{"everyMinutes":60}</c>) or a "due-date" rule's
    /// lookahead window (<c>{"daysBefore":1}</c>). Null/empty for event-driven triggers that don't need
    /// one. Read by the background sweeps (<c>ScheduledAutomationBackgroundService</c>,
    /// <c>DueDateBackgroundService</c>), not by <see cref="AutomationEngine"/>.</summary>
    public string? TriggerConfigJson { get; private set; }

    public bool IsEnabled { get; private set; }

    /// <summary>Incremented on every <see cref="Update"/>; the paired
    /// <see cref="AutomationRuleVersion"/> row snapshots the state BEFORE the increment, so version N's
    /// row is "what the rule looked like before it became version N+1".</summary>
    public int Version { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static AutomationRule Create(
        Guid id, Guid workspaceId, string name, string triggerType,
        string? conditionJson, string? actionJson, Guid createdBy, DateTimeOffset nowUtc, string? triggerConfigJson = null)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        ValidateTrigger(triggerType);

        return new AutomationRule(
            id, workspaceId, name.Trim(), triggerType,
            NormalizeJson(conditionJson, "{}"), NormalizeJson(actionJson, "[]"),
            string.IsNullOrWhiteSpace(triggerConfigJson) ? null : triggerConfigJson.Trim(), createdBy, nowUtc);
    }

    /// <summary>Snapshots the current (pre-change) state — used by the caller to build an
    /// <see cref="AutomationRuleVersion"/> row before applying <see cref="Update"/>.</summary>
    public (string Name, string TriggerType, string ConditionJson, string ActionJson, string? TriggerConfigJson, int Version) SnapshotForVersioning()
        => (Name, TriggerType, ConditionJson, ActionJson, TriggerConfigJson, Version);

    public void Update(string? name, string? triggerType, string? conditionJson, string? actionJson, DateTimeOffset nowUtc, string? triggerConfigJson = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(triggerType))
        {
            ValidateTrigger(triggerType);
            TriggerType = triggerType;
        }

        if (conditionJson is not null)
        {
            ConditionJson = NormalizeJson(conditionJson, "{}");
        }

        if (actionJson is not null)
        {
            ActionJson = NormalizeJson(actionJson, "[]");
        }

        if (triggerConfigJson is not null)
        {
            TriggerConfigJson = string.IsNullOrWhiteSpace(triggerConfigJson) ? null : triggerConfigJson.Trim();
        }

        Version += 1;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Restores a prior version's fields (used by the revert endpoint). Counts as a new
    /// <see cref="Update"/> (increments <see cref="Version"/>) — reverting is itself a change, so its
    /// own prior state is captured too, same as any other edit.</summary>
    public void RestoreFrom(AutomationRuleVersion snapshot, DateTimeOffset nowUtc)
        => Update(snapshot.Name, snapshot.TriggerType, snapshot.ConditionJson, snapshot.ActionJson, nowUtc, snapshot.TriggerConfigJson ?? string.Empty);

    public void Enable(DateTimeOffset nowUtc)
    {
        IsEnabled = true;
        UpdatedAtUtc = nowUtc;
    }

    public void Disable(DateTimeOffset nowUtc)
    {
        IsEnabled = false;
        UpdatedAtUtc = nowUtc;
    }

    private static void ValidateTrigger(string triggerType)
    {
        if (!WorkspaceEvent.Types.All.Contains(triggerType))
        {
            throw new ValidationAppException($"Unknown automation trigger type '{triggerType}'.");
        }
    }

    private static string NormalizeJson(string? json, string fallback)
        => string.IsNullOrWhiteSpace(json) ? fallback : json.Trim();
}

/// <summary>A snapshot of an <see cref="AutomationRule"/>'s fields taken immediately before an
/// edit, so a rule's change history is auditable and revertible (mirrors the DocumentVersion
/// shape). Immutable once written.</summary>
public sealed class AutomationRuleVersion : Entity, IWorkspaceOwned
{
    private AutomationRuleVersion()
    {
    }

    private AutomationRuleVersion(
        Guid id, Guid workspaceId, Guid ruleId, int version, string name, string triggerType,
        string conditionJson, string actionJson, string? triggerConfigJson, Guid changedByUserId, DateTimeOffset changedAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        RuleId = ruleId;
        Version = version;
        Name = name;
        TriggerType = triggerType;
        ConditionJson = conditionJson;
        ActionJson = actionJson;
        TriggerConfigJson = triggerConfigJson;
        ChangedByUserId = changedByUserId;
        ChangedAtUtc = changedAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid RuleId { get; private set; }
    public int Version { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TriggerType { get; private set; } = string.Empty;
    public string ConditionJson { get; private set; } = "{}";
    public string ActionJson { get; private set; } = "[]";
    public string? TriggerConfigJson { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public DateTimeOffset ChangedAtUtc { get; private set; }

    public static AutomationRuleVersion Capture(Guid id, Guid workspaceId, Guid ruleId, AutomationRule rule, Guid changedByUserId, DateTimeOffset changedAtUtc)
    {
        var (name, triggerType, conditionJson, actionJson, triggerConfigJson, version) = rule.SnapshotForVersioning();
        return new AutomationRuleVersion(id, workspaceId, ruleId, version, name, triggerType, conditionJson, actionJson, triggerConfigJson, changedByUserId, changedAtUtc);
    }
}

/// <summary>A record of one rule evaluation for one triggering event. Idempotent on (rule, event id) at
/// creation; a Failed run may subsequently be retried (see <see cref="Attempts"/>/<see cref="NextRetryAtUtc"/>)
/// until it either succeeds or is dead-lettered.</summary>
public sealed class AutomationRun : Entity, IWorkspaceOwned
{
    private AutomationRun()
    {
    }

    private AutomationRun(
        Guid id, Guid workspaceId, Guid ruleId, Guid eventId,
        AutomationRunStatus status, string? detail, DateTimeOffset occurredAtUtc,
        string eventType, string entityType, Guid entityId, Guid actorUserId, string dataJson)
        : base(id)
    {
        WorkspaceId = workspaceId;
        RuleId = ruleId;
        EventId = eventId;
        Status = status;
        Detail = detail;
        OccurredAtUtc = occurredAtUtc;
        EventType = eventType;
        EntityType = entityType;
        EntityId = entityId;
        ActorUserId = actorUserId;
        DataJson = dataJson;
        Attempts = 1;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid RuleId { get; private set; }

    /// <summary>The triggering <see cref="Planvexa.SharedContracts.Events.WorkspaceEvent"/> id; unique per rule.</summary>
    public Guid EventId { get; private set; }

    public AutomationRunStatus Status { get; private set; }
    public string? Detail { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>The triggering event's shape, persisted so a Failed run can be reconstructed
    /// and retried without the original (ephemeral, outbox-derived) WorkspaceEvent still being available.</summary>
    public string EventType { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string DataJson { get; private set; } = "{}";

    /// <summary>Number of execution attempts made so far (starts at 1 — the initial dispatch counts as attempt 1).</summary>
    public int Attempts { get; private set; }

    /// <summary>When set (only while <see cref="Status"/> is <see cref="AutomationRunStatus.Failed"/>),
    /// the retry sweep should try this run again at or after this instant.</summary>
    public DateTimeOffset? NextRetryAtUtc { get; private set; }

    public static AutomationRun Record(
        Guid id, Guid workspaceId, Guid ruleId, WorkspaceEvent workspaceEvent,
        AutomationRunStatus status, string? detail, DateTimeOffset occurredAtUtc)
    {
        var dataJson = System.Text.Json.JsonSerializer.Serialize(workspaceEvent.Data);
        return new AutomationRun(
            id, workspaceId, ruleId, workspaceEvent.EventId, status, Truncate(detail), occurredAtUtc,
            workspaceEvent.EventType, workspaceEvent.EntityType, workspaceEvent.EntityId, workspaceEvent.ActorUserId, dataJson);
    }

    /// <summary>Rebuilds the <see cref="WorkspaceEvent"/> this run was dispatched for, from persisted
    /// context — used by the retry sweep.</summary>
    public WorkspaceEvent ToWorkspaceEvent()
    {
        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(DataJson) ?? new Dictionary<string, string>();
        return new WorkspaceEvent(EventId, WorkspaceId, EventType, EntityType, EntityId, ActorUserId, data);
    }

    /// <summary>Applies the outcome of a retry attempt: success clears the retry state (terminal), failure
    /// either schedules the next attempt (with exponential backoff, capped) or — once
    /// <paramref name="maxAttempts"/> is reached — moves the run to <see cref="AutomationRunStatus.DeadLetter"/>.</summary>
    public void ApplyRetryOutcome(bool success, string? detail, int maxAttempts, DateTimeOffset nowUtc)
    {
        Attempts += 1;
        Detail = Truncate(detail);
        if (success)
        {
            Status = AutomationRunStatus.Success;
            NextRetryAtUtc = null;
            return;
        }

        if (Attempts >= maxAttempts)
        {
            Status = AutomationRunStatus.DeadLetter;
            NextRetryAtUtc = null;
            return;
        }

        Status = AutomationRunStatus.Failed;
        // Exponential backoff capped at 1 hour: 2, 4, 8, ... minutes.
        var backoffMinutes = Math.Min(60, Math.Pow(2, Attempts));
        NextRetryAtUtc = nowUtc.AddMinutes(backoffMinutes);
    }

    /// <summary>Sets the initial retry schedule for a run recorded as Failed on its first (dispatch-time)
    /// attempt.</summary>
    public void ScheduleFirstRetry(DateTimeOffset nowUtc)
        => NextRetryAtUtc = nowUtc.AddMinutes(2);

    /// <summary>Workspace-admin manual retry (dead-letter recovery): re-arms one more attempt immediately,
    /// regardless of the max-attempts cap having been reached.</summary>
    public void RearmForManualRetry(DateTimeOffset nowUtc)
    {
        Status = AutomationRunStatus.Failed;
        NextRetryAtUtc = nowUtc;
    }

    private static string? Truncate(string? detail)
        => detail is { Length: > 1000 } ? detail[..1000] : detail;
}
