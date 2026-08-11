namespace Planvexa.Api.Outbox;

using System.Text.Json;
using Planvexa.BuildingBlocks.Outbox;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.Modules.Forms.Domain.Events;
using Planvexa.Modules.TimeTracking.Domain.Events;
using Planvexa.Modules.WorkManagement.Domain.Events;
using Planvexa.SharedContracts.Events;
using Planvexa.SharedContracts.IntegrationEvents;

/// <summary>
/// Translates an outbox <see cref="OutboxMessage"/> carrying a known WorkManagement task integration
/// event into the module-agnostic <see cref="WorkspaceEvent"/> envelope consumed by the automation and
/// webhook dispatchers. Returns null for messages the workflow subsystem does not react to.
///
/// The composition root is allowed to reference module event types (it is not a module); this keeps the
/// Automations/Integrations modules dependent only on <see cref="WorkspaceEvent"/> (AGENTS.md rule 7).
/// </summary>
public static class WorkspaceEventTranslator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static WorkspaceEvent? Translate(OutboxMessage message)
    {
        if (message.Type == typeof(TaskCreatedIntegrationEvent).FullName)
        {
            var e = Deserialize<TaskCreatedIntegrationEvent>(message.Payload);
            return new WorkspaceEvent(e.EventId, e.WorkspaceId, WorkspaceEvent.Types.TaskCreated,
                "Task", e.TaskId, e.CreatedByUserId,
                new Dictionary<string, string> { ["listId"] = e.ListId.ToString(), ["title"] = e.Title });
        }

        if (message.Type == typeof(TaskCompletedIntegrationEvent).FullName)
        {
            var e = Deserialize<TaskCompletedIntegrationEvent>(message.Payload);
            return new WorkspaceEvent(e.EventId, e.WorkspaceId, WorkspaceEvent.Types.TaskCompleted,
                "Task", e.TaskId, e.CompletedByUserId, new Dictionary<string, string>());
        }

        if (message.Type == typeof(TaskStatusChangedIntegrationEvent).FullName)
        {
            var e = Deserialize<TaskStatusChangedIntegrationEvent>(message.Payload);
            return new WorkspaceEvent(e.EventId, e.WorkspaceId, WorkspaceEvent.Types.TaskStatusChanged,
                "Task", e.TaskId, e.ChangedByUserId,
                new Dictionary<string, string>
                {
                    ["fromStatusId"] = e.FromStatusId.ToString(),
                    ["toStatusId"] = e.ToStatusId.ToString(),
                });
        }

        if (message.Type == typeof(TaskAssignedIntegrationEvent).FullName)
        {
            var e = Deserialize<TaskAssignedIntegrationEvent>(message.Payload);
            return new WorkspaceEvent(e.EventId, e.WorkspaceId, WorkspaceEvent.Types.TaskAssigned,
                "Task", e.TaskId, e.AssignedByUserId,
                new Dictionary<string, string> { ["assigneeUserId"] = e.AssigneeUserId.ToString() });
        }

        // Always raised with the system actor (forms are anonymous) — see
        // AutomationDispatcher's loop-guard carve-out for form.submitted. EntityId is the CREATED TASK
        // (not the form) whenever one exists: AutomationDispatcher's generic action vocabulary
        // (set_status/assign/add_tag/notify) all operate on "the task at workspaceEvent.EntityId" — a
        // rule reacting to form.submitted needs that to be the task the submission just created, exactly
        // like every other trigger. Falls back to the form id only in the (rare) case no task was
        // created, so actions simply no-op instead of acting on a wrong id.
        if (message.Type == typeof(FormSubmittedIntegrationEvent).FullName)
        {
            var e = Deserialize<FormSubmittedIntegrationEvent>(message.Payload);
            return new WorkspaceEvent(e.EventId, e.WorkspaceId, WorkspaceEvent.Types.FormSubmitted,
                e.CreatedTaskId is not null ? "Task" : "Form", e.CreatedTaskId ?? e.FormId, PlatformActors.System,
                new Dictionary<string, string>
                {
                    ["formId"] = e.FormId.ToString(),
                    ["submissionId"] = e.SubmissionId.ToString(),
                });
        }

        // A comment was posted on a task (Collaboration module). The domain entity already
        // raised this integration event (search indexing needed it); this is the first translator
        // case for it, making comment.created usable as an automation/webhook trigger.
        if (message.Type == typeof(CommentPostedIntegrationEvent).FullName)
        {
            var e = Deserialize<CommentPostedIntegrationEvent>(message.Payload);
            return new WorkspaceEvent(e.EventId, e.WorkspaceId, WorkspaceEvent.Types.CommentCreated,
                "Task", e.TaskId, e.AuthorUserId,
                new Dictionary<string, string> { ["commentId"] = e.CommentId.ToString() });
        }

        // A time entry (timer stop or manual entry) was logged against a task (TimeTracking
        // module). Only raised when the entry is linked to a task — see TimeEntryLoggedIntegrationEvent's
        // doc comment.
        if (message.Type == typeof(TimeEntryLoggedIntegrationEvent).FullName)
        {
            var e = Deserialize<TimeEntryLoggedIntegrationEvent>(message.Payload);
            return new WorkspaceEvent(e.EventId, e.WorkspaceId, WorkspaceEvent.Types.TimeEntryLogged,
                "Task", e.TaskId, e.UserId,
                new Dictionary<string, string>
                {
                    ["entryId"] = e.EntryId.ToString(),
                    ["durationSeconds"] = e.DurationSeconds.ToString(),
                });
        }

        return null;
    }

    private static T Deserialize<T>(string payload)
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidOperationException($"Could not deserialize outbox payload as {typeof(T).Name}.");
}
