namespace Planvexa.Modules.Collaboration.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Files;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Collaboration.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// File attachments on a Comment: metadata in <c>collab.comment_attachments</c> (workspace-scoped, RLS
/// enforced), bytes in <see cref="IFileStorage"/>, content magic-byte validated and malware-scanned
/// before being saved — the same pipeline as WorkManagement's <c>AttachmentService</c> for Task
/// attachments (see that class's doc comment). Collaboration doesn't depend on the WorkManagement
/// module, so the small pipeline is duplicated here — the same convention Documents/Clips/Forms/Chat
/// already follow for their own attachment uploads.
/// </summary>
public sealed class CommentAttachmentService(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    ICommentStore comments,
    ICommentAttachmentStore attachments,
    IWorkspaceAccessQuery access,
    ILinkedResourceAccessQuery linkedResources,
    IFileStorage storage,
    IMalwareScanner scanner,
    IRealtimeNotifier realtime,
    IAuditWriter audit,
    IIdGenerator ids,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public const long MaxAttachmentBytes = 25L * 1024 * 1024;

    public async Task<CommentAttachmentDto> UploadAsync(
        Guid commentId, string? fileName, string? contentType, long sizeBytes, Stream content, CancellationToken ct = default)
    {
        if (sizeBytes <= 0)
        {
            throw new ValidationAppException("The uploaded file is empty.");
        }

        if (sizeBytes > MaxAttachmentBytes)
        {
            throw new ValidationAppException($"Attachments are limited to {MaxAttachmentBytes / (1024 * 1024)} MB.");
        }

        var comment = await comments.FindAsync(commentId, ct) ?? throw new NotFoundException("Comment not found.");
        var callerAccess = await access.GetAccessAsync(comment.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null || callerAccess.Role < WorkspaceRole.Member)
        {
            throw new ForbiddenException("You do not have permission to attach files here.");
        }

        // Task-level privacy/ACL check — mirrors LoadWithCommentAsync's read-path gate: workspace
        // membership alone must not let a member attach files to a private Task's comment thread
        // they have no grant on.
        if (!await linkedResources.CanViewAsync(comment.WorkspaceId, currentUser.UserId, LinkedResourceTypes.Task, comment.TaskId, ct))
        {
            throw new ForbiddenException("You do not have permission to access this task.");
        }

        var id = ids.NewId();
        var safeName = SanitizeFileName(fileName);
        var storagePath = $"workspaces/{comment.WorkspaceId}/comment-attachments/{id}/{safeName}";
        var validatedContent = await FileContentValidator.ValidateAsync(content, safeName, contentType, ct);
        await scanner.EnsureCleanAsync(validatedContent, ct);
        await storage.SaveAsync(storagePath, validatedContent, ct);

        var attachment = new CommentAttachment(
            id, comment.WorkspaceId, comment.Id, safeName,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            sizeBytes, storagePath, currentUser.UserId, clock.UtcNow);

        attachments.Add(attachment);
        audit.Write("comment.attachment_added", nameof(CommentAttachment), id, new { commentId, safeName, sizeBytes });
        await unitOfWork.SaveChangesAsync(ct);
        await NotifyRealtimeAsync(comment, ct);
        return ToDto(attachment);
    }

    public async Task<(CommentAttachmentDto Attachment, Stream Content)> DownloadAsync(Guid id, CancellationToken ct = default)
    {
        var (attachment, _) = await LoadWithCommentAsync(id, ct);
        return (ToDto(attachment), await storage.OpenReadAsync(attachment.StoragePath, ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var (attachment, comment) = await LoadWithCommentAsync(id, ct);
        var callerAccess = await access.GetAccessAsync(comment.WorkspaceId, currentUser.UserId, ct);
        var isUploader = attachment.UploadedByUserId == currentUser.UserId;
        if (callerAccess is null || (!isUploader && callerAccess.Role < WorkspaceRole.Admin))
        {
            throw new ForbiddenException("Only the uploader or an admin can delete this attachment.");
        }

        attachments.Remove(attachment);
        audit.Write("comment.attachment_removed", nameof(CommentAttachment), id, new { attachment.CommentId, attachment.FileName });
        await unitOfWork.SaveChangesAsync(ct);
        await NotifyRealtimeAsync(comment, ct);

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
    }

    /// <summary>Best-effort live refresh: the same "Comment" entity/"updated" action CommentService
    /// uses for edits/reactions, so apps/web's useRealtime.invalidateFor refetches the comment thread
    /// on every connected client without a dedicated attachment realtime case.</summary>
    private Task NotifyRealtimeAsync(Comment comment, CancellationToken ct)
        => realtime.NotifyAsync(new RealtimeEvent(
            comment.WorkspaceId, "Comment", comment.Id, "updated", null, workspaceAccessor.Current.CorrelationId), ct);

    /// <summary>Loads the attachment together with its owning comment, gating on both workspace
    /// membership AND the comment's task-level privacy/ACL (via the cross-module
    /// <see cref="ILinkedResourceAccessQuery"/> resolver, AGENTS.md rule 7) — mirrors WorkManagement's
    /// own AttachmentService.LoadWithTaskAsync, which closed the exact same gap for Task attachments:
    /// a coarse workspace-role check alone let any member read/download an attachment on a comment
    /// thread of a private Task they have no grant on.</summary>
    private async Task<(CommentAttachment Attachment, Comment Comment)> LoadWithCommentAsync(Guid id, CancellationToken ct)
    {
        var attachment = await attachments.FindAsync(id, ct) ?? throw new NotFoundException("Attachment not found.");
        var comment = await comments.FindAsync(attachment.CommentId, ct) ?? throw new NotFoundException("Attachment not found.");
        var callerAccess = await access.GetAccessAsync(comment.WorkspaceId, currentUser.UserId, ct);
        if (callerAccess is null)
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }

        if (!await linkedResources.CanViewAsync(comment.WorkspaceId, currentUser.UserId, LinkedResourceTypes.Task, comment.TaskId, ct))
        {
            throw new ForbiddenException("You do not have permission to access this task.");
        }

        return (attachment, comment);
    }

    /// <summary>Same sanitization as WorkManagement's AttachmentService.SanitizeFileName — module
    /// isolation means Collaboration can't reference that internal method, same as every other
    /// attachment-uploading module's own copy (see e.g. DocumentService.SanitizeFileName).</summary>
    private static string SanitizeFileName(string? fileName)
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

    private static CommentAttachmentDto ToDto(CommentAttachment a) => new(
        a.Id, a.CommentId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedByUserId, a.CreatedAtUtc);
}
