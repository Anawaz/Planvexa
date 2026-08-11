namespace Planvexa.Modules.Governance.Application;

using Planvexa.Modules.Governance.Domain;

public interface ISecuritySettingsStore
{
    void Add(EnterpriseSecuritySettings settings);
    Task<EnterpriseSecuritySettings?> FindAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IExportJobStore
{
    void Add(ExportJob job);
    Task<ExportJob?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ExportJob>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Lists pending jobs across workspaces for the background worker. Implementations must use
    /// IgnoreQueryFilters so workspace-scoped filters do not hide work from other workspaces.
    /// </summary>
    Task<IReadOnlyList<ExportJob>> ListPendingAsync(int max, CancellationToken ct = default);
}

public interface IWorkspaceIpAllowRuleStore
{
    void Add(WorkspaceIpAllowRule rule);
    void Remove(WorkspaceIpAllowRule rule);
    Task<WorkspaceIpAllowRule?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkspaceIpAllowRule>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IRetentionPolicyStore
{
    void Add(RetentionPolicy policy);
    Task<RetentionPolicy?> FindAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Lists all retention policies across workspaces for the background purge worker. Implementations must
    /// use IgnoreQueryFilters so the worker sees every workspace's policy.
    /// </summary>
    Task<IReadOnlyList<RetentionPolicy>> ListAllAsync(CancellationToken ct = default);
}

