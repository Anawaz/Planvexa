namespace Planvexa.Modules.Documents.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Documents.Authorization;
using Planvexa.Modules.Documents.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Public, view-only share links for documents — same domain shape, expiration/revocation/password
/// support, and endpoint conventions as Collaboration's task <c>ShareLinkService</c>, duplicated here
/// because Documents cannot depend on the Collaboration module (AGENTS.md rule 7; see DocumentComment's
/// doc comment for the identical precedent). Unlike the task version, there is no guest-comment or
/// access-log sub-feature — public document sharing is view-only, matching what was actually asked for.
/// </summary>
public sealed class DocumentShareLinkService(DocumentsServiceContext ctx, IDocumentStore docs, IDocumentShareLinkStore links)
    : DocumentsServiceBase(ctx)
{
    public async Task<DocumentShareLinkDto> CreateAsync(Guid documentId, int? expiresInDays, string? password, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        DocumentsAuthorizer.EnsureEdit(role);

        var document = await docs.FindAsync(documentId, ct) ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        EnsureCanShare(document, role);

        var validFor = expiresInDays is > 0 ? TimeSpan.FromDays(expiresInDays.Value) : (TimeSpan?)null;
        var (link, rawToken) = DocumentShareLink.Create(NewId(), workspaceId, document.Id, UserId, Now, validFor);
        if (!string.IsNullOrEmpty(password))
        {
            link.SetPassword(password);
        }

        links.Add(link);
        Audit("docs.document.shared", "DocumentShareLink", link.Id, new { documentId = document.Id, passwordProtected = link.RequiresPassword });
        await SaveAsync(ct);

        return new DocumentShareLinkDto(link.Id, link.DocumentId, rawToken, $"/public/documents/{rawToken}", link.ExpiresAtUtc, link.RequiresPassword);
    }

    public async Task<IReadOnlyList<DocumentShareLinkDto>> ListForDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        DocumentsAuthorizer.EnsureRead(role);

        var document = await docs.FindAsync(documentId, ct) ?? throw new NotFoundException("Document not found.");
        EnsureInWorkspace(document, workspaceId);
        EnsureCanShare(document, role);

        var list = await links.ListForDocumentAsync(documentId, ct);
        // The token is only returned on creation; later reads redact it, same as the task share list.
        return list.Where(l => !l.IsRevoked)
            .Select(l => new DocumentShareLinkDto(l.Id, l.DocumentId, string.Empty, "/public/documents/…", l.ExpiresAtUtc, l.RequiresPassword))
            .ToList();
    }

    public async Task RevokeAsync(Guid shareId, CancellationToken ct)
    {
        var link = await links.FindAsync(shareId, ct) ?? throw new NotFoundException("Share link not found.");
        DocumentsAuthorizer.EnsureEdit((await AccessAsync(link.WorkspaceId, ct))?.Role);

        link.Revoke();
        Audit("docs.document.share_revoked", "DocumentShareLink", link.Id);
        await SaveAsync(ct);
    }

    /// <summary>
    /// Anonymous read path. Resolves the link by raw token, verifies the password when required,
    /// establishes the link's workspace context, then returns ONLY the shared document's rendered
    /// Markdown projection — never other documents, comments, versions, or workspace data.
    /// </summary>
    public async Task<SharedDocumentAccessResult> GetSharedDocumentAsync(string rawToken, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return SharedDocumentAccessResult.NotFound;
        }

        var link = await links.FindByTokenHashAsync(DocumentShareLink.HashToken(rawToken), ct);
        if (link is null || !link.IsUsable(Now))
        {
            return SharedDocumentAccessResult.NotFound;
        }

        SetLinkWorkspaceContext(link);

        if (link.RequiresPassword)
        {
            if (string.IsNullOrEmpty(password))
            {
                return SharedDocumentAccessResult.PasswordRequired;
            }

            if (!link.VerifyPassword(password))
            {
                return SharedDocumentAccessResult.InvalidPassword;
            }
        }

        var document = await docs.FindAsync(link.DocumentId, ct);
        if (document is null)
        {
            return SharedDocumentAccessResult.NotFound;
        }

        return new SharedDocumentAccessResult(
            DocumentShareAccessStatus.Ok,
            new SharedDocumentDto(document.Id, document.Title, LexicalMarkdown.ToMarkdown(document.Content), document.UpdatedAtUtc));
    }

    private static void EnsureInWorkspace(Document document, Guid workspaceId)
    {
        if (document.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Document not found.");
        }
    }

    /// <summary>Same rule as DocumentService.EnsureCanModifyPrivate: a private document may only be
    /// shared by its owner or a workspace administrator — sharing publicly is at least as sensitive as
    /// editing.</summary>
    private void EnsureCanShare(Document document, WorkspaceRole? role)
    {
        if (document.IsPrivate && document.OwnerUserId != UserId && !DocumentsAuthorizer.CanManage(role))
        {
            throw new ForbiddenException("Only the document owner or a workspace administrator can share this private document.");
        }
    }

    private void SetLinkWorkspaceContext(DocumentShareLink link)
        => Ctx.WorkspaceAccessor.Set(new WorkspaceContext(
            workspaceId: link.WorkspaceId,
            userId: Guid.Empty,
            membershipId: null,
            role: string.Empty,
            permissions: new HashSet<string>(),
            entitlements: new HashSet<string>(),
            correlationId: Ctx.WorkspaceAccessor.Current.CorrelationId));
}
