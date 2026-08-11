namespace Planvexa.Modules.Documents.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
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
        return list.Where(d => d.CanBeViewedBy(UserId)).Select(ToSummaryDto).ToList();
    }

    public async Task<DocumentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        document.EnsureViewableBy(UserId);
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
        EnsureCanModifyPrivate(document, role);

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
        EnsureCanModifyPrivate(document, role);

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
        document.EnsureViewableBy(UserId);
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
        EnsureCanModifyPrivate(document, role);

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
    /// membership + DocumentsAuthorizer + Document.CanBeViewedBy checks GetAsync/UpdateAsync already apply.
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
        if (document is null || document.WorkspaceId != workspace.WorkspaceId || !document.CanBeViewedBy(UserId))
        {
            return new CollaborationAccessDto(false, false, UserId);
        }

        var canEdit = DocumentsAuthorizer.CanEdit(role)
            && (!document.IsPrivate || document.OwnerUserId == UserId || DocumentsAuthorizer.CanManage(role));
        return new CollaborationAccessDto(true, canEdit, UserId);
    }

    /// <summary>Walks the Lexical content tree and emits Markdown (see LexicalMarkdown).</summary>
    public async Task<string> ExportMarkdownAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var document = await docs.FindAsync(id, ct)
            ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        document.EnsureViewableBy(UserId);

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

    private void EnsureCanModifyPrivate(Document document, WorkspaceRole? role)
    {
        if (document.IsPrivate && document.OwnerUserId != UserId && !DocumentsAuthorizer.CanManage(role))
        {
            throw new ForbiddenException("Only the document owner or a workspace administrator can edit this private document.");
        }
    }

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

        document.EnsureViewableBy(UserId);

        var template = Domain.DocumentTemplate.Create(NewId(), workspaceId, name, document.Content, UserId, Now);
        templates.Add(template);
        Audit("docs.document_template.created", "DocumentTemplate", template.Id, new { template.Name, sourceDocumentId = documentId });
        await SaveAsync(ct);
        return new DocumentTemplateDto(template.Id, template.Name, template.CreatedAtUtc);
    }
}
