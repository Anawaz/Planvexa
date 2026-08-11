namespace Planvexa.Modules.TimeTracking.Application.Services;

using System.Globalization;
using System.Text;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.TimeTracking.Authorization;
using Planvexa.Modules.TimeTracking.Domain;
using Planvexa.SharedContracts.Users;
using Planvexa.SharedContracts.Work;

public enum ReportGrouping
{
    Project,
    Task,
    User,
}

/// <summary>Time reporting projections. All money is decimal and reconciles exactly with entries.</summary>
public sealed class TimeReportService(
    TimeServiceContext ctx,
    ITimeEntryStore entries,
    IUserDirectory users,
    ITaskDirectory taskDirectory,
    IBudgetStore budgets) : TimeServiceBase(ctx)
{
    public async Task<IReadOnlyList<ReportRowDto>> ReportAsync(ReportGrouping groupBy, DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? tagId = null, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        var access = await AccessAsync(workspaceId, ct);
        TimeAuthorizer.EnsureManage(access?.Role);

        var all = await entries.QueryAsync(workspaceId, userId: null, taskId: null, fromUtc, toUtc, tagId, ct);
        var completed = all.Where(e => !e.IsRunning).ToList();

        // The key stays an id (stable for CSV and any caller joining back); the label is what a
        // human reads, so it is resolved through the cross-module directories rather than echoing
        // the id back to the UI.
        var groups = groupBy switch
        {
            ReportGrouping.User => await GroupAsync(
                completed,
                e => e.UserId.ToString(),
                async (e, token) => await UserLabelAsync(e.UserId, token),
                ct),
            ReportGrouping.Task => await GroupAsync(
                completed,
                e => e.TaskId?.ToString() ?? "none",
                async (e, token) => e.TaskId is null ? "No task" : await TaskLabelAsync(e.TaskId.Value, token),
                ct),
            _ => await GroupByListAsync(completed, ct),
        };

        return groups
            .Select(g => new ReportRowDto(
                g.Key.Key,
                g.Key.Label,
                TimeMath.Hours(g.Sum(e => e.DurationSeconds)),
                TimeMath.Hours(g.Where(e => e.IsBillable).Sum(e => e.DurationSeconds)),
                g.Sum(e => TimeMath.Amount(e.DurationSeconds, e.CostRate)),
                g.Where(e => e.IsBillable).Sum(e => TimeMath.Amount(e.DurationSeconds, e.BillingRate))))
            .OrderByDescending(r => r.Hours)
            .ToList();
    }

    public async Task<string> ExportCsvAsync(ReportGrouping groupBy, DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? tagId = null, CancellationToken ct = default)
    {
        var rows = await ReportAsync(groupBy, fromUtc, toUtc, tagId, ct);
        var sb = new StringBuilder();
        sb.AppendLine("Key,Label,Hours,BillableHours,Cost,Revenue");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(',',
                Csv(r.Key), Csv(r.Label),
                r.Hours.ToString(CultureInfo.InvariantCulture),
                r.BillableHours.ToString(CultureInfo.InvariantCulture),
                r.Cost.ToString(CultureInfo.InvariantCulture),
                r.Revenue.ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Accounting-system export: one row per completed entry (not grouped, unlike <see cref="ExportCsvAsync"/>)
    /// in the column layout a QuickBooks Online "Transaction Pro Importer" time-activity import expects
    /// (TXNDATE/NAME/CUSTOMER:JOB/SERVICE ITEM/DURATION/BILLABLESTATUS/NOTES/HOURLYRATE/AMOUNT -- QBO has
    /// no native single-time-activity CSV import of its own; this third-party bridge format is the de
    /// facto standard every QBO time-import tool, including Xero's CSV bank/time bridges, converges on).
    /// Admin+ only (<see cref="TimeAuthorizer.EnsureManage"/>), same gate as every other report/rate
    /// endpoint, since AMOUNT/HOURLYRATE are billing-rate cost data.
    /// </summary>
    public async Task<string> ExportAccountingCsvAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? tagId = null, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var all = await entries.QueryAsync(workspaceId, userId: null, taskId: null, fromUtc, toUtc, tagId, ct);
        var completed = all.Where(e => !e.IsRunning).OrderBy(e => e.StartedAtUtc).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("TxnDate,Employee,CustomerJob,ServiceItem,DurationHours,BillableStatus,Notes,HourlyRate,Amount");
        foreach (var entry in completed)
        {
            var employee = await UserLabelAsync(entry.UserId, ct);
            var customerJob = entry.TaskId is null ? string.Empty : await TaskLabelAsync(entry.TaskId.Value, ct);
            var hours = TimeMath.Hours(entry.DurationSeconds);
            var rate = entry.IsBillable ? entry.BillingRate : 0m;
            var amount = entry.IsBillable ? TimeMath.Amount(entry.DurationSeconds, entry.BillingRate) : 0m;

            sb.AppendLine(string.Join(',',
                entry.StartedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Csv(employee), Csv(customerJob), Csv("Time"),
                hours.ToString(CultureInfo.InvariantCulture),
                entry.IsBillable ? "Billable" : "NotBillable",
                Csv(entry.Description ?? string.Empty),
                rate.ToString(CultureInfo.InvariantCulture),
                amount.ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    public async Task<IReadOnlyList<UtilizationRowDto>> UtilizationAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var all = await entries.QueryAsync(workspaceId, userId: null, taskId: null, fromUtc, toUtc, tagId: null, ct: ct);
        var completed = all.Where(e => !e.IsRunning).ToList();

        return completed
            .GroupBy(e => e.UserId)
            .Select(g =>
            {
                var tracked = TimeMath.Hours(g.Sum(e => e.DurationSeconds));
                var billable = TimeMath.Hours(g.Where(e => e.IsBillable).Sum(e => e.DurationSeconds));
                var utilization = tracked == 0 ? 0m : Math.Round(billable / tracked * 100m, 2, MidpointRounding.AwayFromZero);
                return new UtilizationRowDto(g.Key, tracked, billable, utilization);
            })
            .OrderByDescending(r => r.TrackedHours)
            .ToList();
    }

    /// <summary>
    /// Budget consumption + profitability for one Space/List budget over a date range. Extends
    /// <see cref="ReportAsync"/>'s exact rollup (same <see cref="TimeMath.Hours"/>/<see cref="TimeMath.Amount"/>
    /// reconciliation, same already-resolved <see cref="TimeEntry.BillingRate"/>/<see cref="TimeEntry.CostRate"/>
    /// -- no rate re-derivation) filtered to the budget's scope instead of grouped by project/task/user.
    /// Monetary consumption is measured against <em>cost</em> (labour spend), not billed revenue: a budget
    /// is what the team is allowed to spend delivering the scope, which is a different number from what a
    /// client is billed (see <see cref="BudgetStatus.Profit"/> for the revenue-minus-cost margin).
    /// Admin+ only, same gate as every other rate/report endpoint.
    /// </summary>
    public async Task<BudgetStatusDto> BudgetStatusAsync(Guid budgetId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var budget = await budgets.FindAsync(workspaceId, budgetId, ct) ?? throw new NotFoundException("Budget not found.");

        var all = await entries.QueryAsync(workspaceId, userId: null, taskId: null, fromUtc, toUtc, tagId: null, ct: ct);
        var completed = all.Where(e => !e.IsRunning && e.TaskId is not null).ToList();

        // One task lookup per distinct task, same caching shape as GroupByListAsync below.
        var byTask = new Dictionary<Guid, TaskRef?>();
        var scoped = new List<TimeEntry>(completed.Count);
        foreach (var entry in completed)
        {
            if (!byTask.TryGetValue(entry.TaskId!.Value, out var task))
            {
                task = await taskDirectory.FindAsync(entry.TaskId.Value, ct);
                byTask[entry.TaskId.Value] = task;
            }

            if (task is null)
            {
                continue;
            }

            var matches = budget.ScopeType == BudgetScopeType.List ? task.ListId == budget.ScopeId : task.SpaceId == budget.ScopeId;
            if (matches)
            {
                scoped.Add(entry);
            }
        }

        var trackedSeconds = scoped.Sum(e => e.DurationSeconds);
        var cost = scoped.Sum(e => TimeMath.Amount(e.DurationSeconds, e.CostRate));
        var revenue = scoped.Where(e => e.IsBillable).Sum(e => TimeMath.Amount(e.DurationSeconds, e.BillingRate));

        return TimeMapper.ToDto(BudgetCalculator.Compute(budget, trackedSeconds, cost, revenue));
    }

    /// <summary>
    /// Groups entries by a stable key and resolves one human label per group. Labels are looked up
    /// once per distinct group rather than per entry.
    /// ponytail: one directory call per group; add a batch lookup if a report ever spans enough
    /// distinct users/tasks for the round-trips to show up in traces.
    /// </summary>
    private static async Task<IReadOnlyList<IGrouping<(string Key, string Label), TimeEntry>>> GroupAsync(
        IReadOnlyList<TimeEntry> entries,
        Func<TimeEntry, string> keySelector,
        Func<TimeEntry, CancellationToken, Task<string>> labelResolver,
        CancellationToken ct)
    {
        var labels = new Dictionary<string, string>();
        var keyed = new List<(string Key, TimeEntry Entry)>(entries.Count);

        foreach (var entry in entries)
        {
            var key = keySelector(entry);
            keyed.Add((key, entry));
            if (!labels.ContainsKey(key))
            {
                labels[key] = await labelResolver(entry, ct);
            }
        }

        return keyed
            .GroupBy(x => (x.Key, Label: labels[x.Key]), x => x.Entry)
            .ToList();
    }

    /// <summary>
    /// Groups by the task's containing list. This needs the task resolved before the key is known,
    /// so it pre-resolves each distinct task once instead of using the synchronous key path.
    /// </summary>
    private async Task<IReadOnlyList<IGrouping<(string Key, string Label), TimeEntry>>> GroupByListAsync(
        IReadOnlyList<TimeEntry> entries,
        CancellationToken ct)
    {
        var byTask = new Dictionary<Guid, (string Key, string Label)>();
        var keyed = new List<((string Key, string Label) Group, TimeEntry Entry)>(entries.Count);

        foreach (var entry in entries)
        {
            if (entry.TaskId is null)
            {
                keyed.Add((("none", "No list"), entry));
                continue;
            }

            if (!byTask.TryGetValue(entry.TaskId.Value, out var group))
            {
                var task = await taskDirectory.FindAsync(entry.TaskId.Value, ct);
                group = task is null
                    ? (entry.TaskId.Value.ToString(), $"Deleted task {ShortId(entry.TaskId.Value)}")
                    : (task.ListId.ToString(),
                       string.IsNullOrWhiteSpace(task.ListName) ? $"List {ShortId(task.ListId)}" : task.ListName);
                byTask[entry.TaskId.Value] = group;
            }

            keyed.Add((group, entry));
        }

        return keyed.GroupBy(x => x.Group, x => x.Entry).ToList();
    }

    private async Task<string> UserLabelAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return ShortId(userId);
        }

        return !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName
            : !string.IsNullOrWhiteSpace(user.Email) ? user.Email
            : ShortId(userId);
    }

    private async Task<string> TaskLabelAsync(Guid taskId, CancellationToken ct)
    {
        var task = await taskDirectory.FindAsync(taskId, ct);
        return task is null ? $"Deleted task {ShortId(taskId)}" : task.Title;
    }

    /// <summary>Last segment of a GUID — recognisable in support without printing a full id.</summary>
    private static string ShortId(Guid id) => id.ToString("N")[..8];

    private static string Csv(string value)
        => value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
