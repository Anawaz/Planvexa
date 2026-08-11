namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Files;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;

/// <summary>
/// Task attachments: metadata in <c>work.task_attachments</c> (workspace-scoped, RLS enforced), bytes in
/// <see cref="IFileStorage"/> under a workspace-prefixed path. Content is magic-byte validated against its
/// declared type and malware-scanned before being saved — see
/// <see cref="FileContentValidator"/> and <see cref="IMalwareScanner"/>.
/// </summary>
public sealed class AttachmentService(
    WorkServiceContext ctx, IWorkItemStore tasks, IAttachmentStore attachments, IFileStorage storage, IMalwareScanner scanner)
    : WorkServiceBase(ctx)
{
    public const long MaxAttachmentBytes = 25L * 1024 * 1024;

    public async Task<AttachmentDto> UploadAsync(
        Guid taskId, string? fileName, string? contentType, long sizeBytes, Stream content, CancellationToken ct = default)
    {
        if (sizeBytes <= 0)
        {
            throw new ValidationAppException("The uploaded file is empty.");
        }

        if (sizeBytes > MaxAttachmentBytes)
        {
            throw new ValidationAppException($"Attachments are limited to {MaxAttachmentBytes / (1024 * 1024)} MB.");
        }

        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(task.WorkspaceId, ct))?.Role);

        var id = NewId();
        var safeName = SanitizeFileName(fileName);
        var storagePath = $"workspaces/{task.WorkspaceId}/attachments/{id}/{safeName}";
        var validatedContent = await FileContentValidator.ValidateAsync(content, safeName, contentType, ct);
        await scanner.EnsureCleanAsync(validatedContent, ct);
        await storage.SaveAsync(storagePath, validatedContent, ct);

        var attachment = new TaskAttachment(
            id, task.WorkspaceId, task.Id, safeName,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            sizeBytes, storagePath, UserId, Now);

        attachments.Add(attachment);
        Activity(task.WorkspaceId, task.Id, "attachment_added", safeName);
        Audit("task.attachment_added", nameof(TaskAttachment), id, new { taskId, safeName, sizeBytes });
        await SaveAsync(ct);
        await NotifyRealtimeAsync(task.WorkspaceId, task.Id, "updated", ct);
        return WorkMapper.ToDto(attachment);
    }

    public async Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(task.WorkspaceId, ct))?.Role);

        return (await attachments.ListForTaskAsync(taskId, ct)).Select(WorkMapper.ToDto).ToList();
    }

    public async Task<(AttachmentDto Attachment, Stream Content)> DownloadAsync(Guid id, CancellationToken ct = default)
    {
        var attachment = await LoadAsync(id, ct);
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(attachment.WorkspaceId, ct))?.Role);

        return (WorkMapper.ToDto(attachment), await storage.OpenReadAsync(attachment.StoragePath, ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var attachment = await LoadAsync(id, ct);
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(attachment.WorkspaceId, ct))?.Role);

        attachments.Remove(attachment);
        Activity(attachment.WorkspaceId, attachment.TaskId, "attachment_removed", attachment.FileName);
        Audit("task.attachment_removed", nameof(TaskAttachment), id, new { attachment.TaskId, attachment.FileName });
        await SaveAsync(ct);

        // Best effort: the row is the source of truth, an orphaned blob is harmless.
        try
        {
            await storage.DeleteAsync(attachment.StoragePath, ct);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        await NotifyRealtimeAsync(attachment.WorkspaceId, attachment.TaskId, "updated", ct);
    }

    /// <summary>
    /// Strips any directory component and filesystem-hostile characters, then caps the length at the
    /// column width. The storage root guard in <see cref="IFileStorage"/> is the real containment;
    /// this only keeps names usable and the Content-Disposition header sane.
    /// </summary>
    internal static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Trim();
        var separator = name.LastIndexOfAny(['/', '\\', ':']);
        if (separator >= 0)
        {
            name = name[(separator + 1)..];
        }

        name = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim('.', ' ');

        if (name.Length > 260)
        {
            // Keep the tail so the extension survives.
            name = name[^260..];
        }

        return name.Length == 0 ? "file" : name;
    }

    private async Task<TaskAttachment> LoadAsync(Guid id, CancellationToken ct)
        => await attachments.FindAsync(id, ct)
            ?? throw new NotFoundException("Attachment not found.");
}
