namespace Planvexa.Infrastructure.Platform;

using Microsoft.EntityFrameworkCore;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.Infrastructure.Persistence;

public sealed record InstanceLogPage(IReadOnlyList<InstanceLogEntry> Items, int Total);

/// <summary>
/// Read side of the host console's instance log store. Every filter is applied in the database — the
/// table is the one in this schema that can plausibly reach millions of rows within its retention
/// window, so paging it in memory would be the wrong shape from day one.
///
/// No RLS and no ambient workspace involved (see script 0096): the only caller is the host-admin
/// endpoint, and authorization is the endpoint policy.
/// </summary>
public sealed class InstanceLogQueries(PlanvexaDbContext db)
{
    private const int MaxPageSize = 500;

    public async Task<InstanceLogPage> SearchAsync(
        string? level, string? category, string? search,
        DateTimeOffset? from, DateTimeOffset? to, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        var query = db.InstanceLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(level))
        {
            // "Warning" means warning-and-worse, which is what an operator scanning for trouble
            // actually wants; an exact-match filter would hide the errors underneath it.
            var atLeast = MinimumLevels(level.Trim());
            query = query.Where(e => atLeast.Contains(e.Level));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var term = $"%{category.Trim()}%";
            query = query.Where(e => EF.Functions.ILike(e.Category, term));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(e => EF.Functions.ILike(e.Message, term)
                || (e.Exception != null && EF.Functions.ILike(e.Exception, term))
                || (e.CorrelationId != null && EF.Functions.ILike(e.CorrelationId, term)));
        }

        if (from is { } fromUtc)
        {
            query = query.Where(e => e.CreatedAtUtc >= fromUtc);
        }

        if (to is { } toUtc)
        {
            query = query.Where(e => e.CreatedAtUtc <= toUtc);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip(Math.Max(0, skip))
            .Take(take <= 0 ? 100 : Math.Min(take, MaxPageSize))
            .ToListAsync(cancellationToken);

        return new InstanceLogPage(items, total);
    }

    /// <summary>
    /// The requested level and everything more severe. Ordered by severity rather than compared
    /// numerically because the column stores the level's NAME — writing the ladder out once here beats
    /// parsing an enum in a query expression EF would then have to translate.
    /// </summary>
    private static string[] MinimumLevels(string level) => level switch
    {
        "Trace" => ["Trace", "Debug", "Information", "Warning", "Error", "Critical"],
        "Debug" => ["Debug", "Information", "Warning", "Error", "Critical"],
        "Information" => ["Information", "Warning", "Error", "Critical"],
        "Warning" => ["Warning", "Error", "Critical"],
        "Error" => ["Error", "Critical"],
        "Critical" => ["Critical"],
        _ => [level],
    };
}
