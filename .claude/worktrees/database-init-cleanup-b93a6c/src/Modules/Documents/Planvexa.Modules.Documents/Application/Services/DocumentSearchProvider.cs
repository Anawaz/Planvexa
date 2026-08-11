namespace Planvexa.Modules.Documents.Application.Services;

using Planvexa.Modules.Documents.Authorization;
using Planvexa.Modules.Documents.Domain;
using Planvexa.SharedContracts.Search;

/// <summary>
/// Cross-module search, extended to also match the document's text content (extracted
/// from the Lexical JSON tree via <see cref="LexicalJson.ExtractPlainText"/> rather than matching the raw
/// JSON, so a match can show a readable snippet) in addition to the title. Every result is still filtered
/// through <see cref="Document.CanBeViewedBy"/> (the same private-owner check <see cref="DocumentService"/>
/// applies to GET/list) before it is returned — see ISearchProvider's doc comment on why this filter is not
/// optional; this is the exact check a prior audit of this roadmap flagged as the recurring bug class
/// (a listing/search path that skips per-resource permission filtering), so it must never be bypassed here.
/// </summary>
public sealed class DocumentSearchProvider(DocumentsServiceContext ctx, IDocumentStore docs)
    : DocumentsServiceBase(ctx), ISearchProvider
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default)
    {
        var workspace = Ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return [];
        }

        var role = (await AccessAsync(workspace.WorkspaceId, cancellationToken))?.Role;
        if (!DocumentsAuthorizer.CanRead(role))
        {
            return [];
        }

        var list = await docs.ListByWorkspaceAsync(workspace.WorkspaceId, cancellationToken);
        return list
            .Where(d => d.CanBeViewedBy(UserId))
            .Select(d => (Document: d, PlainText: LexicalJson.ExtractPlainText(d.Content)))
            .Where(x => x.Document.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.PlainText.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Document.UpdatedAtUtc)
            .Take(limit)
            .Select(x => new SearchHit("Document", x.Document.Id, x.Document.Title, Snippet(x.PlainText, term), null))
            .ToList();
    }

    private static string? Snippet(string plainText, string term)
    {
        if (plainText.Length == 0)
        {
            return null;
        }

        var index = plainText.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        var start = index < 0 ? 0 : Math.Max(0, index - 40);
        var length = Math.Min(140, plainText.Length - start);
        var snippet = plainText.Substring(start, length).Replace('\n', ' ').Trim();
        return start > 0 ? $"…{snippet}" : snippet;
    }
}
