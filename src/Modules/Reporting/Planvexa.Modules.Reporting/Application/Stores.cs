namespace Planvexa.Modules.Reporting.Application;

using Planvexa.Modules.Reporting.Domain;

public interface IDashboardStore
{
    void Add(Dashboard dashboard);
    void Remove(Dashboard dashboard);
    Task<Dashboard?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Dashboard?> FindWithWidgetsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Dashboard>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IPortfolioStore
{
    void Add(Portfolio portfolio);
    void Remove(Portfolio portfolio);
    Task<Portfolio?> FindWithMembersAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Portfolio>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IRiskStore
{
    void Add(Risk risk);
    void Remove(Risk risk);
    Task<Risk?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Risk>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IScheduledReportStore
{
    void Add(ScheduledReport report);
    void Remove(ScheduledReport report);
    Task<ScheduledReport?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledReport>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Cross-workspace read for the scheduler (mirrors IDigestPreferenceStore.ListEnabledAsync).</summary>
    Task<IReadOnlyList<ScheduledReport>> ListEnabledAsync(CancellationToken ct = default);
}
