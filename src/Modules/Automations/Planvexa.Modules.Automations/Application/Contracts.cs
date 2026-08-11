namespace Planvexa.Modules.Automations.Application;

// ---- DTOs ----
public sealed record AutomationRuleDto(
    Guid Id, string Name, string TriggerType, bool IsEnabled, string ConditionJson, string ActionJson,
    string? TriggerConfigJson, int Version);

public sealed record AutomationRunDto(
    Guid Id, Guid RuleId, string Status, string? Detail, DateTimeOffset OccurredAtUtc, int Attempts, DateTimeOffset? NextRetryAtUtc);

public sealed record AutomationRuleVersionDto(
    int Version, string Name, string TriggerType, string ConditionJson, string ActionJson,
    string? TriggerConfigJson, Guid ChangedByUserId, DateTimeOffset ChangedAtUtc);

public sealed record AutomationTemplateDto(string Key, string Name, string Description, string TriggerType, string ConditionJson, string ActionJson);

/// <summary>dry-run result: which conditions matched and which actions WOULD fire, with no side
/// effects. <see cref="WouldExecute"/> is a plain textual summary of each parsed action (type + value),
/// not an execution — no cross-module call is made.</summary>
public sealed record AutomationDryRunResultDto(bool ConditionsMatched, IReadOnlyList<string> WouldExecute);

// ---- Commands ----
public sealed record CreateAutomationCommand(string Name, string TriggerType, string? ConditionJson, string? ActionJson, string? TriggerConfigJson = null);

public sealed record UpdateAutomationCommand(string? Name, string? TriggerType, string? ConditionJson, string? ActionJson, string? TriggerConfigJson = null);

/// <summary>Dry-run input: simulates the rule's trigger firing with the given sample event data (e.g.
/// <c>{"toStatusId":"..."}</c>) against an optional real task id (used only for a friendlier action
/// preview — e.g. resolving "assign to X" — never for an actual write).</summary>
public sealed record DryRunAutomationCommand(IReadOnlyDictionary<string, string>? SampleEventData, Guid? SampleTaskId);
