namespace Planvexa.Modules.Forms.Application.Services;

using Planvexa.Modules.Forms.Authorization;
using Planvexa.SharedContracts.Search;

/// <summary>
/// Cross-module search: title matches over this workspace's forms. Forms have no per-resource
/// privacy of their own (see <see cref="FormsAuthorizer"/> — every workspace member, including Guests,
/// may read any form; that coarse workspace-role gate IS this module's real read-permission check), so
/// filtering here is exactly what <see cref="FormService.ListAsync"/> already does.
/// </summary>
public sealed class FormSearchProvider(FormsServiceContext ctx, IFormStore forms)
    : FormsServiceBase(ctx), ISearchProvider
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default)
    {
        var workspace = Ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return [];
        }

        var role = (await AccessAsync(workspace.WorkspaceId, cancellationToken))?.Role;
        if (!FormsAuthorizer.CanRead(role))
        {
            return [];
        }

        var list = await forms.ListByWorkspaceAsync(workspace.WorkspaceId, cancellationToken);
        return list
            .Where(f => f.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(f => new SearchHit("Form", f.Id, f.Title, null, null))
            .ToList();
    }
}
