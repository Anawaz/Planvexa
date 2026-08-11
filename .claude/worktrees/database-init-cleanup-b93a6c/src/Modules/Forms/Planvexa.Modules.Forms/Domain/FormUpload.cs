namespace Planvexa.Modules.Forms.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// Metadata for a file uploaded to a <see cref="FormFieldType.FileUpload"/> field via
/// the pre-submission anonymous upload endpoint. Bytes live in <c>IFileStorage</c>; this row is the
/// pointer + the id the client echoes back as that field's submitted value. Not attached to any task
/// until the surrounding submission is accepted (<c>PublicFormService.SubmitAsync</c> calls
/// <c>ITaskWriteApi.AttachFileAsync</c> with this row's storage path — no byte copy needed since both
/// modules share the same <c>IFileStorage</c> abstraction).
/// </summary>
public sealed class FormUpload : Entity, IWorkspaceOwned
{
    private FormUpload()
    {
    }

    public FormUpload(
        Guid id, Guid workspaceId, Guid formId, string storagePath, string fileName,
        string contentType, long sizeBytes, DateTimeOffset createdAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        FormId = formId;
        StoragePath = storagePath;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid FormId { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
