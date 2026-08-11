namespace Planvexa.Modules.Integrations.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Domain;

/// <summary>
/// Implements the OAuth2 authorization-code flow (Planvexa as the OAuth PROVIDER for third-party apps
/// integrating WITH Planvexa — see <see cref="OAuthApplication"/>'s doc comment). Two distinct trust
/// contexts:
///  - <see cref="AuthorizeAsync"/> runs under an already-authenticated Planvexa user's own session/ambient
///    workspace (the normal middleware pipeline already resolved it) — it is the user consenting to the
///    app's requested scopes for their current workspace.
///  - <see cref="ExchangeAuthorizationCodeAsync"/> and <see cref="RefreshAsync"/> run with NO ambient
///    workspace (the caller is the third-party app's backend, authenticated only by its own
///    client_id/client_secret) — the workspace is resolved entirely from the authorization code / refresh
///    token, never from caller input (AGENTS.md rule 5), and stamped onto the ambient context before the
///    token write so it satisfies the workspace_isolation RLS policy the same way
///    <c>AccessTokenVerifier</c>'s doc comment describes for personal access tokens.
/// </summary>
public sealed class OAuthAuthorizationService(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    IIdGenerator ids,
    IClock clock,
    IOAuthApplicationStore applications,
    IOAuthAuthorizationCodeStore codes,
    IOAuthTokenStore tokens,
    IAuditWriter audit,
    IUnitOfWork unitOfWork)
{
    public async Task<OAuthAuthorizeResultDto> AuthorizeAsync(OAuthAuthorizeCommand command, CancellationToken ct)
    {
        var workspace = workspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            throw new ForbiddenException("An X-Workspace header identifying the target workspace is required.");
        }

        var app = await applications.FindByClientIdAsync(command.ClientId, ct);
        if (app is null || !app.IsActive || app.WorkspaceId != workspace.WorkspaceId)
        {
            throw new NotFoundException("OAuth application not found in this workspace.");
        }

        if (!app.IsRedirectUriAllowed(command.RedirectUri))
        {
            throw new ValidationAppException("redirect_uri is not registered for this application.");
        }

        var grantedScopes = app.FilterScopes(command.Scopes);
        if (grantedScopes.Count == 0)
        {
            throw new ValidationAppException("None of the requested scopes are permitted for this application.");
        }

        var (code, raw) = OAuthAuthorizationCode.Create(
            ids.NewId(), workspace.WorkspaceId, app.Id, currentUser.UserId, command.RedirectUri, grantedScopes, clock.UtcNow);
        codes.Add(code);
        audit.Write("integrations.oauth.authorized", "OAuthApplication", app.Id, new { app.Name, grantedScopes });
        await unitOfWork.SaveChangesAsync(ct);

        return new OAuthAuthorizeResultDto(raw, command.RedirectUri);
    }

    public async Task<OAuthTokenResultDto> ExchangeAuthorizationCodeAsync(
        string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct)
    {
        var app = await AuthenticateClientAsync(clientId, clientSecret, ct);

        var authCode = await codes.FindByHashAsync(SecretCrypto.Hash(code ?? string.Empty), ct);
        if (authCode is null || authCode.ApplicationId != app.Id || !authCode.IsRedeemable(clock.UtcNow)
            || !string.Equals(authCode.RedirectUri, redirectUri, StringComparison.Ordinal))
        {
            throw new ValidationAppException("invalid_grant");
        }

        authCode.MarkUsed(clock.UtcNow);
        BindAmbientWorkspace(authCode.WorkspaceId, authCode.UserId);

        var (token, rawAccess, rawRefresh) = OAuthToken.Create(
            ids.NewId(), authCode.WorkspaceId, app.Id, authCode.UserId, authCode.Scopes, clock.UtcNow);
        tokens.Add(token);
        audit.Write("integrations.oauth.token_issued", "OAuthApplication", app.Id, new { grant = "authorization_code", token.ScopesCsv });
        await unitOfWork.SaveChangesAsync(ct);

        return ToTokenResult(token, rawAccess, rawRefresh);
    }

    public async Task<OAuthTokenResultDto> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct)
    {
        var app = await AuthenticateClientAsync(clientId, clientSecret, ct);

        var token = await tokens.FindByRefreshTokenHashAsync(SecretCrypto.Hash(refreshToken ?? string.Empty), ct);
        if (token is null || token.ApplicationId != app.Id || !token.IsRefreshTokenUsable(clock.UtcNow))
        {
            throw new ValidationAppException("invalid_grant");
        }

        BindAmbientWorkspace(token.WorkspaceId, token.UserId);
        var (rawAccess, rawRefresh) = token.Rotate(clock.UtcNow);
        audit.Write("integrations.oauth.token_refreshed", "OAuthApplication", app.Id, new { grant = "refresh_token" });
        await unitOfWork.SaveChangesAsync(ct);

        return ToTokenResult(token, rawAccess, rawRefresh);
    }

    private async Task<OAuthApplication> AuthenticateClientAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        var app = await applications.FindByClientIdAsync(clientId ?? string.Empty, ct);
        if (app is null || !app.IsActive || !app.VerifySecret(clientSecret ?? string.Empty))
        {
            throw new ValidationAppException("invalid_client");
        }

        return app;
    }

    /// <summary>Stamps the token's own workspace onto the ambient context (no interactive session set one
    /// here) so the subsequent write satisfies the hardened workspace_isolation RLS policy — see this
    /// class's doc comment and <c>AccessTokenVerifier</c>'s identical pattern.</summary>
    private void BindAmbientWorkspace(Guid workspaceId, Guid userId)
        => workspaceAccessor.Set(new WorkspaceContext(
            workspaceId, userId, membershipId: null, role: string.Empty,
            permissions: new HashSet<string>(), entitlements: new HashSet<string>(),
            correlationId: Guid.CreateVersion7().ToString()));

    private OAuthTokenResultDto ToTokenResult(OAuthToken token, string rawAccess, string rawRefresh)
        => new(rawAccess, "Bearer", Math.Max(1, (int)(token.ExpiresAtUtc - clock.UtcNow).TotalSeconds), rawRefresh, string.Join(' ', token.Scopes));
}
