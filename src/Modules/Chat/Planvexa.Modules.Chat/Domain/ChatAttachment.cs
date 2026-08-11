namespace Planvexa.Modules.Chat.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// Metadata for a file attached to a chat message. The bytes live in <c>IFileStorage</c> at
/// <see cref="StoragePath"/> (same abstraction WorkManagement's TaskAttachment uses); this row is the
/// workspace-scoped, RLS-protected handle to them.
/// </summary>
public sealed class ChatAttachment : Entity, IWorkspaceOwned
{
    private ChatAttachment()
    {
    }

    public ChatAttachment(
        Guid id,
        Guid workspaceId,
        Guid messageId,
        string fileName,
        string contentType,
        long sizeBytes,
        string storagePath,
        Guid uploadedByUserId,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        MessageId = messageId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StoragePath = storagePath;
        UploadedByUserId = uploadedByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid MessageId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public Guid UploadedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
