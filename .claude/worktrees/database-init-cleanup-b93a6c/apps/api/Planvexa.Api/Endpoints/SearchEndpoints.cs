namespace Planvexa.Api.Endpoints;

using Planvexa.Api.Search;

/// <summary>
/// Global cross-module search across the caller's current workspace: tasks, lists, folders, spaces
/// (WorkManagement), documents, comments, chat channels/messages, members, teams, dashboards and forms —
/// each fanned out to its owning module's own permission-filtered <c>ISearchProvider</c> (Goals
/// is not covered — that resource type does not exist yet).
/// </summary>
public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/search", async (string? q, int? limit, SearchAggregator aggregator, CancellationToken ct) =>
                Results.Ok(await aggregator.SearchAsync(q, limit, ct)))
            .RequireAuthorization()
            .WithName("GlobalSearch");
    }
}
