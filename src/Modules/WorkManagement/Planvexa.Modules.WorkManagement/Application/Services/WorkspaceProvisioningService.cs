namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.Modules.WorkManagement.Domain;

/// <summary>Ensures a workspace has its default status scheme (created lazily on first use).</summary>
public sealed class WorkspaceProvisioningService(WorkServiceContext ctx, IStatusSchemeStore schemes)
    : WorkServiceBase(ctx)
{
    public async Task<StatusScheme> EnsureDefaultSchemeAsync(Guid workspaceId, CancellationToken ct)
    {
        var existing = await schemes.FindDefaultAsync(workspaceId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var scheme = StatusScheme.CreateDefault(NewId(), workspaceId, NewId);
        schemes.Add(scheme);
        return scheme;
    }

    /// <summary>The scheme a Space resolves to: its own override, else the workspace default.</summary>
    public async Task<StatusScheme> EffectiveSchemeAsync(Space space, CancellationToken ct)
        => (space.StatusSchemeId is { } id ? await schemes.FindAsync(id, ct) : null)
           ?? await EnsureDefaultSchemeAsync(space.WorkspaceId, ct);
}
