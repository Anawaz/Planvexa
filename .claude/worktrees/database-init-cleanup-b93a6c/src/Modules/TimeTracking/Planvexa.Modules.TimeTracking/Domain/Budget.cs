namespace Planvexa.Modules.TimeTracking.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A monetary and/or time cap scoped to a Space or a List ("project" -- see <see cref="MemberRate.ProjectId"/>).
/// TimeReportService computes consumption against the cap and profitability (billable-rate revenue
/// minus cost-rate cost) for the scope's tracked time in a date range; this entity only holds the cap
/// itself. <see cref="ScopeId"/> is a WorkManagement Space.Id or TaskList.Id, resolved through
/// <c>ITaskDirectory</c> rather than a foreign key (AGENTS.md rule 7).
/// </summary>
public sealed class Budget : Entity, IAggregateRoot, IWorkspaceOwned
{
    private Budget()
    {
    }

    private Budget(Guid id, Guid workspaceId, BudgetScopeType scopeType, Guid scopeId, string name,
        decimal? monetaryCapAmount, long? timeCapSeconds, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ScopeType = scopeType;
        ScopeId = scopeId;
        Name = name;
        MonetaryCapAmount = monetaryCapAmount;
        TimeCapSeconds = timeCapSeconds;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public BudgetScopeType ScopeType { get; private set; }
    public Guid ScopeId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal? MonetaryCapAmount { get; private set; }
    public long? TimeCapSeconds { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static Budget Create(
        Guid id, Guid workspaceId, BudgetScopeType scopeType, Guid scopeId, string name,
        decimal? monetaryCapAmount, long? timeCapSeconds, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(scopeId, nameof(scopeId));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        if (monetaryCapAmount is null && timeCapSeconds is null)
        {
            throw new ValidationAppException("A budget needs a monetary cap, a time cap, or both.");
        }

        if (monetaryCapAmount is < 0)
        {
            throw new ValidationAppException("The monetary cap cannot be negative.");
        }

        if (timeCapSeconds is < 0)
        {
            throw new ValidationAppException("The time cap cannot be negative.");
        }

        return new Budget(id, workspaceId, scopeType, scopeId, name.Trim(), monetaryCapAmount, timeCapSeconds, nowUtc);
    }

    public void Update(string name, decimal? monetaryCapAmount, long? timeCapSeconds, DateTimeOffset nowUtc)
    {
        if (monetaryCapAmount is null && timeCapSeconds is null)
        {
            throw new ValidationAppException("A budget needs a monetary cap, a time cap, or both.");
        }

        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
        MonetaryCapAmount = monetaryCapAmount;
        TimeCapSeconds = timeCapSeconds;
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>
/// Budget consumption + profitability for one scope over a date range. <see cref="Hours"/>/<see cref="Cost"/>/
/// <see cref="Revenue"/> reuse the exact rollup TimeReportService already computes per report row; this
/// projects the same numbers against a <see cref="Budget"/>'s cap instead of grouping by project/task/user.
/// </summary>
public sealed record BudgetStatus(
    Guid BudgetId, string Name, BudgetScopeType ScopeType, Guid ScopeId,
    decimal? MonetaryCapAmount, long? TimeCapSeconds,
    decimal Hours, decimal Cost, decimal Revenue,
    decimal? MonetaryConsumedPercent, decimal? TimeConsumedPercent)
{
    public decimal Profit => Revenue - Cost;
}

/// <summary>
/// Pure consumption/profitability math for a <see cref="Budget"/>, split out from
/// <c>TimeReportService.BudgetStatusAsync</c> so it is unit-testable without a database (mirrors
/// <see cref="TimeMath"/> / <see cref="MissingTimeReminderPolicy"/>). Monetary consumption is measured
/// against cost (labour spend), not billed revenue -- see <see cref="TimeReportService"/>'s doc comment
/// on <c>BudgetStatusAsync</c> for why.
/// </summary>
public static class BudgetCalculator
{
    public static BudgetStatus Compute(Budget budget, long trackedSeconds, decimal cost, decimal revenue)
    {
        var hours = TimeMath.Hours(trackedSeconds);

        var monetaryPct = budget.MonetaryCapAmount is { } cap && cap > 0
            ? Math.Round(cost / cap * 100m, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        var timePct = budget.TimeCapSeconds is { } capSeconds && capSeconds > 0
            ? Math.Round((decimal)trackedSeconds / capSeconds * 100m, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        return new BudgetStatus(
            budget.Id, budget.Name, budget.ScopeType, budget.ScopeId, budget.MonetaryCapAmount, budget.TimeCapSeconds,
            hours, cost, revenue, monetaryPct, timePct);
    }
}
