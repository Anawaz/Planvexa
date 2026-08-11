namespace Planvexa.Modules.TimeTracking.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.TimeTracking.Application;
using Planvexa.Modules.TimeTracking.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Shared dependencies + helpers for TimeTracking services.</summary>
public sealed class TimeServiceContext(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    IIdGenerator ids,
    IClock clock,
    IAuditWriter audit,
    IWorkspaceAccessQuery access,
    ITimePolicyStore policies,
    IMemberRateStore rates,
    IRealtimeNotifier realtime,
    IUnitOfWork unitOfWork)
{
    public IWorkspaceContextAccessor WorkspaceAccessor => workspaceAccessor;
    public ICurrentUser CurrentUser => currentUser;
    public IIdGenerator Ids => ids;
    public IClock Clock => clock;
    public IAuditWriter Audit => audit;
    public IWorkspaceAccessQuery Access => access;
    public ITimePolicyStore Policies => policies;
    public IMemberRateStore Rates => rates;
    public IRealtimeNotifier Realtime => realtime;
    public IUnitOfWork UnitOfWork => unitOfWork;
}

public abstract class TimeServiceBase(TimeServiceContext ctx)
{
    protected TimeServiceContext Ctx => ctx;
    protected Guid UserId => ctx.CurrentUser.UserId;
    protected DateTimeOffset Now => ctx.Clock.UtcNow;
    protected Guid NewId() => ctx.Ids.NewId();

    protected Guid RequireWorkspace()
    {
        var workspace = ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            throw new ForbiddenException("An X-Workspace header identifying the target workspace is required.");
        }

        return workspace.WorkspaceId;
    }

    protected Task<WorkspaceAccess?> AccessAsync(Guid workspaceId, CancellationToken ct)
        => ctx.Access.GetAccessAsync(workspaceId, UserId, ct);

    protected void Audit(string action, string entityType, Guid? entityId, object? data = null)
        => ctx.Audit.Write(action, entityType, entityId, data);

    protected Task SaveAsync(CancellationToken ct) => ctx.UnitOfWork.SaveChangesAsync(ct);

    /// <summary>Broadcasts a realtime change signal for a time entry (best-effort; DB stays authoritative).</summary>
    protected Task NotifyRealtimeAsync(Guid workspaceId, Guid entryId, string action, CancellationToken ct)
        => ctx.Realtime.NotifyAsync(
            new RealtimeEvent(workspaceId, "TimeEntry", entryId, action, null, ctx.WorkspaceAccessor.Current.CorrelationId), ct);

    /// <summary>Gets the workspace policy, creating and persisting a default the first time it's needed.</summary>
    protected async Task<TimePolicy> GetOrCreatePolicyAsync(Guid workspaceId, CancellationToken ct)
    {
        var policy = await ctx.Policies.FindAsync(workspaceId, ct);
        if (policy is null)
        {
            policy = TimePolicy.CreateDefault(NewId(), workspaceId);
            ctx.Policies.Add(policy);
        }

        return policy;
    }

    /// <summary>Resolves the effective (billing, cost) rate for a member, preferring a project override.</summary>
    protected async Task<(decimal Billing, decimal Cost)> ResolveRatesAsync(Guid workspaceId, Guid userId, Guid? projectId, CancellationToken ct)
    {
        if (projectId is not null)
        {
            var projectRate = await ctx.Rates.FindAsync(workspaceId, userId, projectId, ct);
            if (projectRate is not null)
            {
                return (projectRate.BillingRate, projectRate.CostRate);
            }
        }

        var defaultRate = await ctx.Rates.FindAsync(workspaceId, userId, null, ct);
        return defaultRate is null ? (0m, 0m) : (defaultRate.BillingRate, defaultRate.CostRate);
    }
}
