namespace Planvexa.Modules.TimeTracking.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.TimeTracking.Authorization;
using Planvexa.Modules.TimeTracking.Domain;

/// <summary>
/// CRUD for Space/List-scoped budgets. Admin+ only -- same gate as rates, policy and
/// reports (<see cref="TimeAuthorizer.EnsureManage"/>), since a budget's cap and consumption expose
/// cost-rate data. See <see cref="TimeReportService.BudgetStatusAsync"/> for the consumption/profitability
/// computation, which extends the same rollup logic <see cref="TimeReportService.ReportAsync"/> already uses.
/// </summary>
public sealed class BudgetService(TimeServiceContext ctx, IBudgetStore budgets) : TimeServiceBase(ctx)
{
    public async Task<IReadOnlyList<BudgetDto>> ListAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);
        var list = await budgets.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(TimeMapper.ToDto).ToList();
    }

    public async Task<BudgetDto> CreateAsync(CreateBudgetCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var existing = await budgets.FindByScopeAsync(workspaceId, command.ScopeType, command.ScopeId, ct);
        if (existing is not null)
        {
            throw new ConflictException("A budget already exists for this Space or List.");
        }

        var budget = Budget.Create(
            NewId(), workspaceId, command.ScopeType, command.ScopeId, command.Name,
            command.MonetaryCapAmount, command.TimeCapSeconds, Now);
        budgets.Add(budget);
        Audit("time.budget_created", "Budget", budget.Id, new { command.ScopeType, command.ScopeId });
        await SaveAsync(ct);
        return TimeMapper.ToDto(budget);
    }

    public async Task<BudgetDto> UpdateAsync(Guid budgetId, UpdateBudgetCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var budget = await budgets.FindAsync(workspaceId, budgetId, ct) ?? throw new NotFoundException("Budget not found.");
        budget.Update(command.Name, command.MonetaryCapAmount, command.TimeCapSeconds, Now);
        Audit("time.budget_updated", "Budget", budget.Id);
        await SaveAsync(ct);
        return TimeMapper.ToDto(budget);
    }

    public async Task DeleteAsync(Guid budgetId, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var budget = await budgets.FindAsync(workspaceId, budgetId, ct) ?? throw new NotFoundException("Budget not found.");
        Audit("time.budget_deleted", "Budget", budget.Id);
        budgets.Remove(budget);
        await SaveAsync(ct);
    }
}
