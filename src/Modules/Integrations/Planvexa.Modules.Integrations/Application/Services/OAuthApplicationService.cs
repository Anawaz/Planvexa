namespace Planvexa.Modules.Integrations.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Authorization;
using Planvexa.Modules.Integrations.Domain;

/// <summary>Manages a workspace's OAuth2 applications (Admin+) — the third-party apps allowed to request
/// scoped access to this workspace via the authorization-code flow.</summary>
public sealed class OAuthApplicationService(
    IntegrationsServiceContext ctx,
    IOAuthApplicationStore applications)
    : IntegrationsServiceBase(ctx)
{
    public async Task<IReadOnlyList<OAuthApplicationDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureManageOAuthApps((await AccessAsync(workspaceId, ct))?.Role);

        var list = await applications.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<CreatedOAuthApplicationDto> CreateAsync(CreateOAuthApplicationCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureManageOAuthApps((await AccessAsync(workspaceId, ct))?.Role);

        var (app, rawSecret) = OAuthApplication.Create(
            NewId(), workspaceId, command.Name, command.RedirectUris, command.AllowedScopes, UserId, Now);
        applications.Add(app);
        Audit("integrations.oauth_app.created", "OAuthApplication", app.Id, new { app.Name, app.AllowedScopesCsv });
        await SaveAsync(ct);

        return new CreatedOAuthApplicationDto(app.Id, app.Name, app.ClientId, rawSecret, app.RedirectUris, app.AllowedScopes, app.CreatedAtUtc);
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureManageOAuthApps((await AccessAsync(workspaceId, ct))?.Role);

        var app = await applications.FindAsync(workspaceId, id, ct) ?? throw new NotFoundException("OAuth application not found.");
        app.Revoke();
        Audit("integrations.oauth_app.revoked", "OAuthApplication", id);
        await SaveAsync(ct);
    }

    private static OAuthApplicationDto ToDto(OAuthApplication a)
        => new(a.Id, a.Name, a.ClientId, a.RedirectUris, a.AllowedScopes, a.IsActive, a.CreatedAtUtc);
}
