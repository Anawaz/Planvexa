namespace Planvexa.Modules.Governance.Application.Services;

using System.Net;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Authorization;
using Planvexa.Modules.Governance.Domain;

/// <summary>
/// Manages a workspace's IP allow list and answers the yes/no question the host
/// middleware (<c>IpAllowListMiddleware</c>) asks on every request: is this source IP allowed to call this
/// workspace's API? Zero rules means unrestricted, matching every other optional per-workspace security
/// feature in this codebase.
/// </summary>
public sealed class IpAllowListService(
    GovernanceServiceContext ctx,
    IWorkspaceIpAllowRuleStore store)
    : GovernanceServiceBase(ctx)
{
    public async Task<IReadOnlyList<IpAllowRuleDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var rules = await store.ListByWorkspaceAsync(workspaceId, ct);
        return rules.Select(ToDto).ToList();
    }

    public async Task<IpAllowRuleDto> AddAsync(AddIpAllowRuleCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var rule = WorkspaceIpAllowRule.Create(NewId(), workspaceId, command.Cidr, command.Description, Now);
        store.Add(rule);
        Audit("governance.ip_allow_rule.added", nameof(WorkspaceIpAllowRule), rule.Id, new { rule.Cidr });
        await SaveAsync(ct);
        return ToDto(rule);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var rule = await store.FindAsync(id, ct) ?? throw new NotFoundException("IP allow rule not found.");
        if (rule.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("IP allow rule not found in this workspace.");
        }

        store.Remove(rule);
        Audit("governance.ip_allow_rule.removed", nameof(WorkspaceIpAllowRule), id, new { rule.Cidr });
        await SaveAsync(ct);
    }

    /// <summary>Called by <c>IpAllowListMiddleware</c> for every request to a resolved workspace — NOT
    /// authorization-gated (unlike every other method here), since this is the access-control check
    /// itself, evaluated before authorization even runs. Returns true (allowed) when the workspace has no
    /// configured rules.</summary>
    public async Task<bool> IsAllowedAsync(Guid workspaceId, IPAddress remoteAddress, CancellationToken ct)
    {
        var rules = await store.ListByWorkspaceAsync(workspaceId, ct);
        return rules.Count == 0 || rules.Any(r => r.Matches(remoteAddress));
    }

    private static IpAllowRuleDto ToDto(WorkspaceIpAllowRule r) => new(r.Id, r.Cidr, r.Description, r.CreatedAtUtc);
}
