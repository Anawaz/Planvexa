namespace Planvexa.Modules.Automations.Application.Services;

using Planvexa.SharedContracts.Events;

/// <summary>
/// A small static catalog of pre-built automation rules a user can instantiate with one click
/// (<see cref="AutomationRuleService.CreateFromTemplateAsync"/>). Deliberately not a templating engine —
/// just fixed name/trigger/condition/action combinations users commonly want, per AGENTS.md rule 16
/// (prefer the simplest thing that works).
/// </summary>
public static class AutomationTemplates
{
    public static readonly IReadOnlyList<AutomationTemplateDto> All = new[]
    {
        new AutomationTemplateDto(
            Key: "notify-assignee-on-status-change",
            Name: "Notify on status change",
            Description: "When a task's status changes, notify a chosen user. Status ids are per-workspace"
                + " (the triggering event carries \"toStatusId\", not a name), so after creating this rule,"
                + " edit its condition to the specific status id (e.g. your workspace's \"Blocked\" status)"
                + " and its action's recipient user id.",
            TriggerType: WorkspaceEvent.Types.TaskStatusChanged,
            ConditionJson: "{}",
            ActionJson: """[{"type":"notify","value":""}]"""),

        new AutomationTemplateDto(
            Key: "tag-overdue-tasks",
            Name: "Auto-tag overdue tasks",
            Description: "When a task's due date arrives without it being completed, tag it \"overdue\".",
            TriggerType: WorkspaceEvent.Types.TaskDueSoon,
            ConditionJson: "{}",
            ActionJson: """[{"type":"add_tag","value":"overdue"}]"""),

        new AutomationTemplateDto(
            Key: "comment-on-completion",
            Name: "Comment when a task is completed",
            Description: "Posts a congratulatory comment on a task the moment it's marked complete.",
            TriggerType: WorkspaceEvent.Types.TaskCompleted,
            ConditionJson: "{}",
            ActionJson: """[{"type":"comment","value":"Nice work — \"{{task.title}}\" is complete!"}]"""),

        new AutomationTemplateDto(
            Key: "sla-breach-alert",
            Name: "Alert on SLA breach",
            Description: "When a task has been in the same status for more than 2 business days (2880 minutes), tag it \"sla-breach\".",
            TriggerType: WorkspaceEvent.Types.TaskSlaBreached,
            ConditionJson: """{"field":"minutesInStatus","gte":"2880"}""",
            ActionJson: """[{"type":"add_tag","value":"sla-breach"}]"""),
    };
}
