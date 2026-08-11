namespace Planvexa.Modules.Whiteboards.Application.Services;

using Planvexa.Modules.Whiteboards.Authorization;
using Planvexa.SharedContracts.Search;

/// <summary>
/// Cross-module search fan-out, extended for whiteboards: matches a whiteboard's name only (its
/// actual shape content is Yjs binary state, not searchable text — see Whiteboard's doc comment). Every
/// result is filtered through <see cref="WhiteboardService.CanAccessAsync"/> — the exact same rule
/// GetAsync/ListAsync apply — before it is returned; this is the recurring bug class a prior audit of this
/// roadmap flagged (a listing/search path that skips per-resource permission filtering), so it must never
/// be bypassed here.
/// </summary>
public sealed class WhiteboardSearchProvider(WhiteboardServiceContext ctx, IWhiteboardStore whiteboards, WhiteboardService whiteboardService)
    : WhiteboardServiceBase(ctx), ISearchProvider
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default)
    {
        var workspace = Ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return [];
        }

        var role = await RoleAsync(workspace.WorkspaceId, cancellationToken);
        if (!WhiteboardsAuthorizer.CanRead(role))
        {
            return [];
        }

        var list = await whiteboards.ListByWorkspaceAsync(workspace.WorkspaceId, cancellationToken);
        var hits = new List<SearchHit>();
        foreach (var wb in list)
        {
            if (hits.Count >= limit)
            {
                break;
            }

            if (!wb.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (await whiteboardService.CanAccessAsync(wb, role, cancellationToken))
            {
                hits.Add(new SearchHit("Whiteboard", wb.Id, wb.Name, wb.IsPrivate ? "Private whiteboard" : "Whiteboard", null));
            }
        }

        return hits;
    }
}
