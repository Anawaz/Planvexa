namespace Planvexa.Modules.Governance.Application.Services;

using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Authorization;
using Planvexa.Modules.Governance.Domain;

/// <summary>Reads and updates a workspace's data-retention policy (Admin+).</summary>
public sealed class RetentionService(
    GovernanceServiceContext ctx,
    IRetentionPolicyStore store)
    : GovernanceServiceBase(ctx)
{
    public async Task<RetentionPolicyDto> GetAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var policy = await store.FindAsync(workspaceId, ct)
            ?? RetentionPolicy.CreateDefault(NewId(), workspaceId, Now);
        return ToDto(policy);
    }

    public async Task<RetentionPolicyDto> UpdateAsync(UpdateRetentionPolicyCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var policy = await store.FindAsync(workspaceId, ct);
        if (policy is null)
        {
            policy = RetentionPolicy.CreateDefault(NewId(), workspaceId, Now);
            store.Add(policy);
        }

        policy.Update(command.DeletedTaskRetentionDays, command.AuditRetentionDays, command.LegalHold, Now);
        Audit("governance.retention.updated", "RetentionPolicy", policy.Id,
            new { policy.DeletedTaskRetentionDays, policy.AuditRetentionDays, policy.LegalHold });
        await SaveAsync(ct);
        return ToDto(policy);
    }

    private static RetentionPolicyDto ToDto(RetentionPolicy p)
        => new(p.DeletedTaskRetentionDays, p.AuditRetentionDays, p.LegalHold);
}
