namespace Planvexa.Modules.Chat.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Files;
using Planvexa.Modules.Chat.Authorization;
using Planvexa.Modules.Chat.Domain;

/// <summary>
/// Chat message attachments: metadata in <c>chat.attachments</c> (workspace-scoped, RLS enforced), bytes
/// in the same <c>IFileStorage</c> abstraction WorkManagement's AttachmentService uses for task
/// attachments, under a workspace-prefixed path.
/// </summary>
public sealed class ChatAttachmentService(ChatServiceContext ctx, IChatMessageStore messages, IChatAttachmentStore attachments, ChatChannelService channelService)
    : ChatServiceBase(ctx)
{
    public const long MaxAttachmentBytes = 25L * 1024 * 1024;

    public async Task<ChatAttachmentDto> UploadAsync(
        Guid messageId, string? fileName, string? contentType, long sizeBytes, Stream content, CancellationToken ct = default)
    {
        if (sizeBytes <= 0)
        {
            throw new ValidationAppException("The uploaded file is empty.");
        }

        if (sizeBytes > MaxAttachmentBytes)
        {
            throw new ValidationAppException($"Attachments are limited to {MaxAttachmentBytes / (1024 * 1024)} MB.");
        }

        var message = await messages.FindAsync(messageId, ct) ?? throw new NotFoundException("Message not found.");
        var (channel, role) = await channelService.LoadForReadAsync(message.ChannelId, ct);
        ChatAuthorizer.EnsureParticipate(role);

        if (message.AuthorUserId != UserId)
        {
            throw new ForbiddenException("Only the message author can attach files to it.");
        }

        var id = NewId();
        var safeName = SanitizeFileName(fileName);
        var storagePath = $"workspaces/{channel.WorkspaceId}/chat-attachments/{id}/{safeName}";
        var validatedContent = await FileContentValidator.ValidateAsync(content, safeName, contentType, ct);
        await Ctx.MalwareScanner.EnsureCleanAsync(validatedContent, ct);
        await Ctx.FileStorage.SaveAsync(storagePath, validatedContent, ct);

        var attachment = new ChatAttachment(
            id, channel.WorkspaceId, message.Id, safeName,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            sizeBytes, storagePath, UserId, Now);

        attachments.Add(attachment);
        Audit("chat.attachment_added", nameof(ChatAttachment), id, new { messageId, safeName, sizeBytes });
        await SaveAsync(ct);
        await NotifyAsync(channel.WorkspaceId, "ChatMessage", message.Id, "updated", ct);
        return ToDto(attachment);
    }

    public async Task<(ChatAttachmentDto Attachment, Stream Content)> DownloadAsync(Guid id, CancellationToken ct = default)
    {
        var attachment = await attachments.FindAsync(id, ct) ?? throw new NotFoundException("Attachment not found.");
        var message = await messages.FindAsync(attachment.MessageId, ct) ?? throw new NotFoundException("Attachment not found.");
        await channelService.LoadForReadAsync(message.ChannelId, ct);

        return (ToDto(attachment), await Ctx.FileStorage.OpenReadAsync(attachment.StoragePath, ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var attachment = await attachments.FindAsync(id, ct) ?? throw new NotFoundException("Attachment not found.");
        var message = await messages.FindAsync(attachment.MessageId, ct) ?? throw new NotFoundException("Attachment not found.");
        var (channel, role) = await channelService.LoadForReadAsync(message.ChannelId, ct);

        if (attachment.UploadedByUserId != UserId && !ChatAuthorizer.IsModerator(role))
        {
            throw new ForbiddenException("Only the uploader or a workspace administrator can remove this attachment.");
        }

        attachments.Remove(attachment);
        Audit("chat.attachment_removed", nameof(ChatAttachment), id, new { attachment.MessageId, attachment.FileName });
        await SaveAsync(ct);

        // Best effort: the row is the source of truth, an orphaned blob is harmless.
        try
        {
            await Ctx.FileStorage.DeleteAsync(attachment.StoragePath, ct);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        await NotifyAsync(channel.WorkspaceId, "ChatMessage", message.Id, "updated", ct);
    }

    /// <summary>Same sanitization as WorkManagement's AttachmentService.SanitizeFileName (module
    /// boundaries mean it can't be shared directly; kept intentionally identical).</summary>
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
            name = name[^260..];
        }

        return name.Length == 0 ? "file" : name;
    }

    private static ChatAttachmentDto ToDto(ChatAttachment a)
        => new(a.Id, a.MessageId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedByUserId, a.CreatedAtUtc);
}
