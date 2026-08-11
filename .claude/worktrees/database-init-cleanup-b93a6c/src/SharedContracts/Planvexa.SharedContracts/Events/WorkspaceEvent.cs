namespace Planvexa.SharedContracts.Events;

/// <summary>
/// Normalized, module-agnostic event envelope used by the workflow subsystem (automations + webhooks).
/// The composition root (API host) translates module integration events (e.g. WorkManagement task
/// events) into this shape so the Automations and Integrations modules depend only on this contract,
/// never on another module's event types (AGENTS.md rule 7).
/// </summary>
public sealed record WorkspaceEvent(
    Guid EventId,
    Guid WorkspaceId,
    string EventType,
    string EntityType,
    Guid EntityId,
    Guid ActorUserId,
    IReadOnlyDictionary<string, string> Data)
{
    /// <summary>Stable event-type identifiers used by triggers and webhook subscriptions.</summary>
    public static class Types
    {
        public const string TaskCreated = "task.created";
        public const string TaskStatusChanged = "task.status_changed";
        public const string TaskAssigned = "task.assigned";
        public const string TaskCompleted = "task.completed";

        /// <summary>A public form submission was accepted. Unlike the Task.* events
        /// above, this is raised with the SYSTEM actor by design (forms are anonymous) — see
        /// AutomationDispatcher's loop-guard carve-out for why that doesn't get it swallowed.</summary>
        public const string FormSubmitted = "form.submitted";

        /// <summary>A comment was posted on a task (Collaboration module).</summary>
        public const string CommentCreated = "comment.created";

        /// <summary>A time entry (timer stop or manual entry) was logged against a task
        /// (TimeTracking module).</summary>
        public const string TimeEntryLogged = "time_entry.logged";

        /// <summary>A task's due date has arrived or is within a rule-configured lookahead
        /// window. Raised by <c>DueDateBackgroundService</c>'s daily sweep, not by any user action —
        /// always carries the SYSTEM actor (see AutomationDispatcher's loop-guard carve-out).</summary>
        public const string TaskDueSoon = "task.due_soon";

        /// <summary>A workspace-configured recurring (cron-like) trigger fired. Raised by
        /// <c>ScheduledAutomationBackgroundService</c>, not tied to any task event — always carries the
        /// SYSTEM actor (see AutomationDispatcher's loop-guard carve-out). EntityType is "AutomationRule"
        /// and EntityId is the rule's own id (there is no triggering task).</summary>
        public const string ScheduleRecurring = "schedule.recurring";

        /// <summary>A task has spent longer than a configured threshold in its current status
        /// (an SLA breach). Raised by <c>SlaBackgroundService</c>'s sweep — always carries the SYSTEM actor
        /// (see AutomationDispatcher's loop-guard carve-out). Data carries "statusName" and
        /// "minutesInStatus" so rule conditions can compare against a threshold (see AutomationEngine's
        /// numeric "gte"/"lte" leaf operators).</summary>
        public const string TaskSlaBreached = "task.sla_breached";

        public static readonly IReadOnlyList<string> All =
            new[]
            {
                TaskCreated, TaskStatusChanged, TaskAssigned, TaskCompleted, FormSubmitted,
                CommentCreated, TimeEntryLogged, TaskDueSoon, ScheduleRecurring, TaskSlaBreached,
            };

        /// <summary>Trigger types that are legitimately raised with the SYSTEM actor by a background
        /// sweep rather than an interactive user or an automation action. AutomationDispatcher's loop
        /// guard (which otherwise ignores every SYSTEM-actor event, since automation actions run as the
        /// system actor and must not re-trigger themselves) carves these out. None of this change's action
        /// vocabulary (set_status/assign/add_tag/notify/email/webhook/custom_field/comment/integration)
        /// emits any of these event types, so the carve-out cannot itself create a loop.</summary>
        public static readonly IReadOnlyList<string> SystemActorTriggers =
            new[] { FormSubmitted, TaskDueSoon, ScheduleRecurring, TaskSlaBreached };
    }
}
