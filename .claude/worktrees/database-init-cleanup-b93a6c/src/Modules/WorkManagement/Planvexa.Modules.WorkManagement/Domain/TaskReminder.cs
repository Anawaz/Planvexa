namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>A per-user reminder for a task, fired once at <see cref="RemindAtUtc"/> as a notification.</summary>
public sealed class TaskReminder : Entity, IWorkspaceOwned
{
    private TaskReminder()
    {
    }

    private TaskReminder(Guid id, Guid workspaceId, Guid taskId, Guid userId, DateTimeOffset remindAtUtc, string? note, DateTimeOffset createdAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        TaskId = taskId;
        UserId = userId;
        RemindAtUtc = remindAtUtc;
        Note = note;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset RemindAtUtc { get; private set; }
    public string? Note { get; private set; }
    public bool IsSent { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static TaskReminder Create(
        Guid id, Guid workspaceId, Guid taskId, Guid userId, DateTimeOffset remindAtUtc, string? note, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(taskId, nameof(taskId));
        Guard.AgainstEmpty(userId, nameof(userId));
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return new TaskReminder(id, workspaceId, taskId, userId, remindAtUtc, trimmed, nowUtc);
    }

    public void MarkSent(DateTimeOffset nowUtc)
    {
        IsSent = true;
        SentAtUtc = nowUtc;
    }
}
