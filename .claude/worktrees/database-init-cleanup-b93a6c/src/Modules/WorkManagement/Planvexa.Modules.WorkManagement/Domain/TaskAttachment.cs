namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// Metadata for a file attached to a task. The bytes themselves live in <c>IFileStorage</c> at
/// <see cref="StoragePath"/>; this row is the workspace-scoped, RLS-protected handle to them.
/// </summary>
public sealed class TaskAttachment : Entity, IWorkspaceOwned
{
    private TaskAttachment()
    {
    }

    public TaskAttachment(
        Guid id,
        Guid workspaceId,
        Guid taskId,
        string fileName,
        string contentType,
        long sizeBytes,
        string storagePath,
        Guid uploadedByUserId,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        TaskId = taskId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StoragePath = storagePath;
        UploadedByUserId = uploadedByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid WorkspaceId { get; private set; }

    public Guid TaskId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string StoragePath { get; private set; } = string.Empty;

    public Guid UploadedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Merge: moves this attachment onto another task.</summary>
    public void ReassignTask(Guid newTaskId) => TaskId = newTaskId;
}
