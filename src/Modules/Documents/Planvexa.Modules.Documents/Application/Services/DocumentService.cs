namespace Planvexa.Modules.Documents.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Files;
using Planvexa.Modules.Documents.Authorization;
using Planvexa.Modules.Documents.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Manages workspace documents and their content versions.</summary>
public sealed class DocumentService(
    DocumentsServiceContext ctx,
    IDocumentStore docs,
    IDocumentTemplateStore templates)
    : DocumentsServiceBase(ctx)
{
    public async Task<IReadOnlyList<DocumentSummaryDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var list = await docs.ListByWorkspaceAsync(workspaceId, ct);
        var visible = new List<Document>();
        foreach (var document in list)
        {
            if (await CanViewAsync(document, ct))
            {
                visible.Add(document);
            }
        }

        return visible.Select(ToSummaryDto).ToList();
    }

    public async Task<DocumentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        await EnsureViewableAsync(document, ct);
        return ToDto(document);
    }

    public async Task<DocumentDto> CreateAsync(CreateDocumentCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var content = command.Content;
        if (command.TemplateId is { } templateId)
        {
            var template = await templates.FindAsync(templateId, ct)
                ?? throw new NotFoundException("Document template not found.");
            if (template.WorkspaceId != workspaceId)
            {
                throw new NotFoundException("Document template not found.");
            }

            content = template.ContentJson;
        }

        if (command.ParentDocumentId is { } parentId)
        {
            await EnsureParentInWorkspaceAsync(parentId, workspaceId, ct);
        }

        var document = Document.Create(
            NewId(),
            workspaceId,
            UserId,
            command.Title,
            content,
            command.IsPrivate,
            command.SpaceId,
            command.ListId,
            command.TaskId,
            Now,
            command.ParentDocumentId);
        docs.Add(document);
        Audit("docs.document.created", "Document", document.Id, new { document.Title, document.IsPrivate, document.SpaceId, document.ListId, document.TaskId, document.ParentDocumentId });
        await SaveAsync(ct);
        return ToDto(document);
    }

    public async Task<DocumentDto> UpdateAsync(Guid id, UpdateDocumentCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        DocumentsAuthorizer.EnsureEdit(role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        await EnsureCanModifyPrivateAsync(document, role, ct);

        document.Update(NewId(), command.Title, command.Content, command.IsPrivate, UserId, Now);
        Audit("docs.document.updated", "Document", document.Id, new { command.Title, HasContent = command.Content is not null, command.IsPrivate });
        await SaveAsync(ct);
        return ToDto(document);
    }

    /// <summary>Re-parents a document in the wiki tree, rejecting a move that would make the
    /// document its own ancestor — same cycle-prevention discipline as the Folder nesting
    /// (<see cref="DocumentHierarchy"/> is a line-for-line port of FolderHierarchy's algorithm).</summary>
    public async Task<DocumentDto> SetParentAsync(Guid id, Guid? newParentDocumentId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        DocumentsAuthorizer.EnsureEdit(role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        await EnsureCanModifyPrivateAsync(document, role, ct);

        if (newParentDocumentId is { } parentId)
        {
            await EnsureParentInWorkspaceAsync(parentId, workspaceId, ct);
        }

        var parentMap = await docs.ListParentMapByWorkspaceAsync(workspaceId, ct);
        if (DocumentHierarchy.CreatesCycle(id, newParentDocumentId, parentMap))
        {
            throw new ValidationAppException("Moving this document there would create a cycle in the document tree.");
        }

        document.SetParent(newParentDocumentId, UserId, Now);
        Audit("docs.document.reparented", "Document", document.Id, new { newParentDocumentId });
        await SaveAsync(ct);
        return ToDto(document);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        DocumentsAuthorizer.EnsureEdit(role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        EnsureOwnerOrAdmin(document, role, "Only the document owner or a workspace administrator can delete this document.");

        docs.Remove(document);
        Audit("docs.document.deleted", "Document", document.Id);
        await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentVersionDto>> GetVersionsAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var document = await docs.FindWithVersionsAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        await EnsureViewableAsync(document, ct);
        return document.Versions
            .OrderByDescending(v => v.CreatedAtUtc)
            .Select(ToVersionDto)
            .ToList();
    }

    public async Task<DocumentDto> RevertAsync(Guid id, Guid versionId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        DocumentsAuthorizer.EnsureEdit(role);

        var document = await docs.FindWithVersionsAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        await EnsureCanModifyPrivateAsync(document, role, ct);

        var target = document.Versions.FirstOrDefault(v => v.Id == versionId)
            ?? throw new NotFoundException("Document version not found.");
        document.Revert(NewId(), target, UserId, Now);
        Audit("docs.document.reverted", "Document", document.Id, new { versionId });
        await SaveAsync(ct);
        return ToDto(document);
    }

    /// <summary>
    /// The single most important check: the ONLY thing the Hocuspocus
    /// collaboration server's onAuthenticate hook trusts before admitting a WebSocket connection into a
    /// document's room. Reachable via the internal endpoint (GET /api/v1/internal/documents/{id}/can-collaborate,
    /// see DocumentEndpoints) which requires the SAME bearer-token authentication as any other endpoint — the
    /// Node service forwards the connecting user's own token, so this runs exactly the same
    /// membership + DocumentsAuthorizer.CanViewAsync/CanModifyPrivateAsync checks GetAsync/UpdateAsync already apply.
    /// Never returns Allowed=true for a workspace mismatch or a private document the caller doesn't own.
    /// </summary>
    public async Task<CollaborationAccessDto> CanCollaborateAsync(Guid id, CancellationToken ct)
    {
        var workspace = Ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return new CollaborationAccessDto(false, false, null);
        }

        var role = (await AccessAsync(workspace.WorkspaceId, ct))?.Role;
        if (!DocumentsAuthorizer.CanRead(role))
        {
            return new CollaborationAccessDto(false, false, UserId);
        }

        var document = await docs.FindAsync(id, ct);
        if (document is null || document.WorkspaceId != workspace.WorkspaceId || !await CanViewAsync(document, ct))
        {
            return new CollaborationAccessDto(false, false, UserId);
        }

        var canEdit = DocumentsAuthorizer.CanEdit(role) && await CanModifyPrivateAsync(document, role, ct);
        return new CollaborationAccessDto(true, canEdit, UserId);
    }

    /// <summary>
    /// Uploads an image dropped/pasted/inserted into a document's rich-text content. Same
    /// no-DB-row shape as WhiteboardService.UploadImageAsync: the returned <paramref name="id"/>-scoped
    /// imageId is embedded directly in the Lexical content (see the editor's ImageNode) as the only record
    /// that the image is still referenced — an image later removed from the content just orphans its blob,
    /// which is harmless (same tradeoff every attachment-delete path in this codebase accepts). Requires the
    /// same edit access as any other content change (EnsureCanModifyPrivate blocks non-owners on a private
    /// document).
    /// </summary>
    public async Task<(Guid ImageId, string ContentType)> UploadImageAsync(
        Guid id, string? contentType, long sizeBytes, Stream content, CancellationToken ct)
    {
        const long maxImageBytes = 25L * 1024 * 1024;
        if (sizeBytes <= 0)
        {
            throw new ValidationAppException("The uploaded image is empty.");
        }

        if (sizeBytes > maxImageBytes)
        {
            throw new ValidationAppException($"Document images are limited to {maxImageBytes / (1024 * 1024)} MB.");
        }

        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        DocumentsAuthorizer.EnsureEdit(role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        await EnsureCanModifyPrivateAsync(document, role, ct);

        var imageId = NewId();
        var safeContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        var validatedContent = await FileContentValidator.ValidateAsync(content, fileName: null, safeContentType, ct);
        await Ctx.MalwareScanner.EnsureCleanAsync(validatedContent, ct);
        await Ctx.FileStorage.SaveAsync(ImagePath(document.WorkspaceId, document.Id, imageId), validatedContent, ct);
        Audit("docs.image_uploaded", "Document", document.Id, new { imageId, sizeBytes });
        return (imageId, safeContentType);
    }

    public async Task<Stream> DownloadImageAsync(Guid id, Guid imageId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        await EnsureViewableAsync(document, ct);

        return await Ctx.FileStorage.OpenReadAsync(ImagePath(document.WorkspaceId, document.Id, imageId), ct);
    }

    private static string ImagePath(Guid workspaceId, Guid documentId, Guid imageId)
        => $"workspaces/{workspaceId}/documents/{documentId}/images/{imageId}";

    /// <summary>
    /// Uploads a file attached to a document's rich-text content (see the editor's FileAttachmentNode).
    /// Same no-DB-row shape as <see cref="UploadImageAsync"/>: the returned attachmentId, together with the
    /// sanitized file name baked into the storage path, is embedded directly in the Lexical content as the
    /// only record the file is still referenced — removing the attachment from the content just orphans its
    /// blob, which is harmless (same tradeoff every attachment-delete path in this codebase accepts).
    /// Requires the same edit access as any other content change.
    /// </summary>
    public async Task<(Guid AttachmentId, string FileName, string ContentType, long SizeBytes)> UploadAttachmentAsync(
        Guid id, string? fileName, string? contentType, long sizeBytes, Stream content, CancellationToken ct)
    {
        const long maxAttachmentBytes = 25L * 1024 * 1024;
        if (sizeBytes <= 0)
        {
            throw new ValidationAppException("The uploaded file is empty.");
        }

        if (sizeBytes > maxAttachmentBytes)
        {
            throw new ValidationAppException($"Document attachments are limited to {maxAttachmentBytes / (1024 * 1024)} MB.");
        }

        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        DocumentsAuthorizer.EnsureEdit(role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        await EnsureCanModifyPrivateAsync(document, role, ct);

        var attachmentId = NewId();
        var safeName = SanitizeFileName(fileName);
        var safeContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        var validatedContent = await FileContentValidator.ValidateAsync(content, safeName, safeContentType, ct);
        await Ctx.MalwareScanner.EnsureCleanAsync(validatedContent, ct);
        await Ctx.FileStorage.SaveAsync(AttachmentPath(document.WorkspaceId, document.Id, attachmentId, safeName), validatedContent, ct);
        Audit("docs.attachment_uploaded", "Document", document.Id, new { attachmentId, safeName, sizeBytes });
        return (attachmentId, safeName, safeContentType, sizeBytes);
    }

    public async Task<Stream> DownloadAttachmentAsync(Guid id, Guid attachmentId, string fileName, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        await EnsureViewableAsync(document, ct);

        return await Ctx.FileStorage.OpenReadAsync(AttachmentPath(document.WorkspaceId, document.Id, attachmentId, SanitizeFileName(fileName)), ct);
    }

    private static string AttachmentPath(Guid workspaceId, Guid documentId, Guid attachmentId, string fileName)
        => $"workspaces/{workspaceId}/documents/{documentId}/attachments/{attachmentId}/{fileName}";

    /// <summary>Strips any directory component and filesystem-hostile characters, then caps the length — a
    /// duplicate of WorkManagement's AttachmentService.SanitizeFileName's small pure logic (this module
    /// can't reference WorkManagement's, see AGENTS.md's module-boundary rule) so the storage path segment
    /// and download file name stay safe, including against a crafted "../" download URL segment.</summary>
    private static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Trim();
        var separator = name.LastIndexOfAny(['/', '\\', ':']);
        if (separator >= 0)
        {
            name = name[(separator + 1)..];
        }

        name = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim('.', ' ');
        if (name.Length > 200)
        {
            name = name[^200..];
        }

        return name.Length == 0 ? "file" : name;
    }

    /// <summary>Walks the Lexical content tree and emits Markdown (see LexicalMarkdown).</summary>
    public async Task<string> ExportMarkdownAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        await EnsureViewableAsync(document, ct);

        return $"# {document.Title}\n\n{LexicalMarkdown.ToMarkdown(document.Content)}";
    }

    private async Task EnsureParentInWorkspaceAsync(Guid parentId, Guid workspaceId, CancellationToken ct)
    {
        var parent = await docs.FindAsync(parentId, ct)
            ?? throw new NotFoundException("Parent document not found.");
        if (parent.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Parent document not found.");
        }
    }

    private Task<bool> CanViewAsync(Document document, CancellationToken ct)
        => DocumentsAuthorizer.CanViewAsync(document, UserId, Ctx.ResourcePermissions, ct);

    private Task EnsureViewableAsync(Document document, CancellationToken ct)
        => DocumentsAuthorizer.EnsureViewableAsync(document, UserId, Ctx.ResourcePermissions, ct);

    private Task<bool> CanModifyPrivateAsync(Document document, WorkspaceRole? role, CancellationToken ct)
        => DocumentsAuthorizer.CanModifyPrivateAsync(document, UserId, role, Ctx.ResourcePermissions, ct);

    private Task EnsureCanModifyPrivateAsync(Document document, WorkspaceRole? role, CancellationToken ct)
        => DocumentsAuthorizer.EnsureCanModifyPrivateAsync(document, UserId, role, Ctx.ResourcePermissions, ct);

    private void EnsureOwnerOrAdmin(Document document, WorkspaceRole? role, string message)
    {
        if (document.OwnerUserId != UserId && !DocumentsAuthorizer.CanManage(role))
        {
            throw new ForbiddenException(message);
        }
    }

    private static void EnsureInWorkspace(Document document, Guid workspaceId)
    {
        if (document.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Document not found.");
        }
    }

    private static DocumentDto ToDto(Document d)
        => new(d.Id, d.Title, d.Content, d.IsPrivate, d.OwnerUserId, d.SpaceId, d.ListId, d.TaskId, d.ParentDocumentId, d.UpdatedAtUtc);

    private static DocumentSummaryDto ToSummaryDto(Document d)
        => new(d.Id, d.Title, d.IsPrivate, d.OwnerUserId, d.SpaceId, d.ListId, d.TaskId, d.ParentDocumentId, d.UpdatedAtUtc);

    private static DocumentVersionDto ToVersionDto(DocumentVersion v)
        => new(v.Id, v.AuthorUserId, v.CreatedAtUtc, Preview(v.Content));

    private static string Preview(string content)
    {
        var text = LexicalJson.ExtractPlainText(content);
        return text.Length <= 200 ? text : text[..200];
    }
}

/// <summary>Reusable document content snapshots (see DocumentTemplate's doc comment for why
/// this isn't WorkTemplate).</summary>
public sealed class DocumentTemplateService(DocumentsServiceContext ctx, IDocumentStore docs, IDocumentTemplateStore templates)
    : DocumentsServiceBase(ctx)
{
    public async Task<IReadOnlyList<DocumentTemplateDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var list = await templates.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(t => new DocumentTemplateDto(t.Id, t.Name, t.CreatedAtUtc)).ToList();
    }

    public async Task<DocumentTemplateDto> CreateFromDocumentAsync(Guid documentId, string name, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var document = await docs.FindAsync(documentId, ct)
            ?? throw new NotFoundException("Document not found.");
        if (document.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Document not found.");
        }

        await DocumentsAuthorizer.EnsureViewableAsync(document, UserId, Ctx.ResourcePermissions, ct);

        var template = Domain.DocumentTemplate.Create(NewId(), workspaceId, name, document.Content, UserId, Now);
        templates.Add(template);
        Audit("docs.document_template.created", "DocumentTemplate", template.Id, new { template.Name, sourceDocumentId = documentId });
        await SaveAsync(ct);
        return new DocumentTemplateDto(template.Id, template.Name, template.CreatedAtUtc);
    }
}
