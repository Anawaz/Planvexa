namespace Planvexa.Modules.Reporting.Application.Services;

using Planvexa.Modules.Reporting.Authorization;
using Planvexa.SharedContracts.Search;

/// <summary>
/// Cross-module search: name matches over this workspace's dashboards, filtered through
/// <see cref="Domain.Dashboard.CanBeViewedBy"/> (the same private-owner check <see cref="DashboardService"/>
/// applies to GET/list) before a single name is returned — see ISearchProvider's doc comment on why this
/// filter is not optional.
/// </summary>
public sealed class DashboardSearchProvider(ReportingServiceContext ctx, IDashboardStore dashboards)
    : ReportingServiceBase(ctx), ISearchProvider
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default)
    {
        var workspace = Ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return [];
        }

        var role = (await AccessAsync(workspace.WorkspaceId, cancellationToken))?.Role;
        if (!ReportingAuthorizer.CanRead(role))
        {
            return [];
        }

        var list = await dashboards.ListByWorkspaceAsync(workspace.WorkspaceId, cancellationToken);
        return list
            .Where(d => d.CanBeViewedBy(UserId) && d.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(d => new SearchHit("Dashboard", d.Id, d.Name, null, null))
            .ToList();
    }
}
