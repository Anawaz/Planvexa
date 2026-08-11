namespace Planvexa.Modules.Collaboration.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// Metadata for a file attached to a <see cref="Comment"/>. Mirrors WorkManagement's TaskAttachment —
/// the bytes themselves live in <c>IFileStorage</c> at <see cref="StoragePath"/>, this row is the
/// workspace-scoped, RLS-protected handle to them (see TaskAttachment's doc comment for the full
/// upload/scan pipeline this reuses). A standalone entity, not part of the <see cref="Comment"/>
/// aggregate — same relationship TaskAttachment has to WorkItem.
/// </summary>
public sealed class CommentAttachment : Entity, IWorkspaceOwned
{
    private CommentAttachment()
    {
    }

    public CommentAttachment(
        Guid id,
        Guid workspaceId,
        Guid commentId,
        string fileName,
        string contentType,
        long sizeBytes,
        string storagePath,
        Guid uploadedByUserId,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        CommentId = commentId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StoragePath = storagePath;
        UploadedByUserId = uploadedByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid WorkspaceId { get; private set; }

    public Guid CommentId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string StoragePath { get; private set; } = string.Empty;

    public Guid UploadedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
