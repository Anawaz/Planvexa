namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Notifications;

/// <summary>Per-user task reminders: create/list/delete, plus the dispatcher entrypoint that fires one.</summary>
public sealed class ReminderService(
    WorkServiceContext ctx,
    IWorkItemStore tasks,
    IReminderStore reminders,
    INotificationPublisher notifications) : WorkServiceBase(ctx)
{
    public async Task<ReminderDto> CreateAsync(CreateReminderCommand command, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(command.TaskId, ct);
        if (task is null || task.IsDeleted)
        {
            throw new NotFoundException("Task not found.");
        }

        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(task.WorkspaceId, ct))?.Role);

        var reminder = TaskReminder.Create(NewId(), task.WorkspaceId, task.Id, UserId, command.RemindAtUtc, command.Note, Now);
        reminders.Add(reminder);
        Audit("task.reminder_created", "TaskReminder", reminder.Id, new { taskId = task.Id, command.RemindAtUtc });
        await SaveAsync(ct);
        return ToDto(reminder);
    }

    public async Task<IReadOnlyList<ReminderDto>> ListForTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(task.WorkspaceId, ct))?.Role);
        var list = await reminders.ListForTaskAsync(taskId, UserId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task DeleteAsync(Guid reminderId, CancellationToken ct = default)
    {
        var reminder = await reminders.FindAsync(reminderId, ct) ?? throw new NotFoundException("Reminder not found.");
        if (reminder.UserId != UserId)
        {
            throw new ForbiddenException("You can only remove your own reminders.");
        }

        reminders.Remove(reminder);
        Audit("task.reminder_deleted", "TaskReminder", reminder.Id, new { taskId = reminder.TaskId });
        await SaveAsync(ct);
    }

    /// <summary>Dispatcher entrypoint (ambient workspace already bound): fire the notification once.</summary>
    public async Task DispatchAsync(TaskReminder reminder, CancellationToken ct = default)
    {
        if (reminder.IsSent)
        {
            return;
        }

        await notifications.PublishAsync(new NotificationRequest(
            reminder.UserId, "task.reminder", "Task", reminder.TaskId, reminder.WorkspaceId,
            $"reminder:{reminder.Id}",
            reminder.Note is null ? null : new Dictionary<string, string> { ["note"] = reminder.Note }), ct);

        reminder.MarkSent(Now);
        await SaveAsync(ct);
    }

    private static ReminderDto ToDto(TaskReminder r) => new(r.Id, r.TaskId, r.RemindAtUtc, r.Note, r.IsSent);
}
