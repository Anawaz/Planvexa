namespace Planvexa.Modules.Documents.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Documents.Authorization;
using Planvexa.Modules.Documents.Domain;

/// <summary>Document comments — a flat, timestamped list gated by the exact same access rule as the
/// document itself (see DocumentsAuthorizer.CanViewAsync's doc comment: a private document's comments are
/// exactly as hidden as the document, ADR-0003 sharing grants included). See DocumentComment's doc comment
/// for why this is a lightweight parallel aggregate rather than reusing Collaboration's Task-comment
/// machinery.</summary>
public sealed class DocumentCommentService(DocumentsServiceContext ctx, IDocumentStore docs, IDocumentCommentStore comments)
    : DocumentsServiceBase(ctx)
{
    public async Task<IReadOnlyList<DocumentCommentDto>> ListAsync(Guid documentId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var document = await LoadViewableAsync(documentId, workspaceId, ct);
        var list = await comments.ListByDocumentAsync(workspaceId, document.Id, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<DocumentCommentDto> AddAsync(Guid documentId, string body, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        DocumentsAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var document = await LoadViewableAsync(documentId, workspaceId, ct);
        var comment = DocumentComment.Create(NewId(), workspaceId, document.Id, UserId, body, Now);
        comments.Add(comment);
        Audit("docs.comment_added", "Document", document.Id);
        await SaveAsync(ct);
        return ToDto(comment);
    }

    private async Task<Document> LoadViewableAsync(Guid documentId, Guid workspaceId, CancellationToken ct)
    {
        var document = await docs.FindAsync(documentId, ct)
            ?? throw new NotFoundException("Document not found.");
        if (document.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Document not found.");
        }

        await DocumentsAuthorizer.EnsureViewableAsync(document, UserId, Ctx.ResourcePermissions, ct);
        return document;
    }

    private static DocumentCommentDto ToDto(DocumentComment c) => new(c.Id, c.AuthorUserId, c.Body, c.CreatedAtUtc);
}
