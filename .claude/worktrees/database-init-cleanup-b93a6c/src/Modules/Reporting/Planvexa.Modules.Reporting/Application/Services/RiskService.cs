namespace Planvexa.Modules.Reporting.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Reporting.Authorization;
using Planvexa.Modules.Reporting.Domain;

/// <summary>Risk register CRUD. Admin+ — mirrors Portfolio's own gate, since a risk
/// is portfolio-management data.</summary>
public sealed class RiskService(ReportingServiceContext ctx, IRiskStore risks) : ReportingServiceBase(ctx)
{
    public async Task<IReadOnlyList<RiskDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);
        var list = await risks.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<RiskDto> CreateAsync(CreateRiskCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var risk = Risk.Create(NewId(), workspaceId, command.Title, command.Description, command.Severity, command.ScopeType, command.ScopeId, UserId, Now);
        risks.Add(risk);
        Audit("reporting.risk_created", "Risk", risk.Id, new { command.Title, command.Severity });
        await SaveAsync(ct);
        return ToDto(risk);
    }

    public async Task<RiskDto> UpdateAsync(Guid id, UpdateRiskCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var risk = await risks.FindAsync(workspaceId, id, ct) ?? throw new NotFoundException("Risk not found.");
        risk.Update(command.Title, command.Description, command.Severity, command.Status, Now);
        Audit("reporting.risk_updated", "Risk", risk.Id);
        await SaveAsync(ct);
        return ToDto(risk);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var risk = await risks.FindAsync(workspaceId, id, ct) ?? throw new NotFoundException("Risk not found.");
        Audit("reporting.risk_deleted", "Risk", risk.Id);
        risks.Remove(risk);
        await SaveAsync(ct);
    }

    private static RiskDto ToDto(Risk r) => new(r.Id, r.Title, r.Description, r.Severity, r.ScopeType, r.ScopeId, r.Status);
}
