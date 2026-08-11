namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Application.Services;

// ---- Request models ----
public sealed record CreateOAuthApplicationRequest(string Name, IReadOnlyList<string> RedirectUris, IReadOnlyList<string> AllowedScopes);
public sealed record OAuthAuthorizeRequest(string ClientId, string RedirectUri, IReadOnlyList<string> Scope);

/// <summary>
/// The token-endpoint request body. Deviates from RFC 6749's application/x-www-form-urlencoded in favor
/// of JSON, matching every other endpoint in this API (this OAuth provider serves apps that integrate
/// WITH Planvexa and are documented against this shape, not arbitrary spec-strict third-party OAuth
/// clients).
/// ponytail: JSON-only token endpoint; add application/x-www-form-urlencoded binding if a real
/// integration partner's OAuth library insists on it.
/// </summary>
public sealed record OAuthTokenRequest(
    string GrantType, string ClientId, string ClientSecret, string? Code, string? RedirectUri, string? RefreshToken);

public sealed record UpdateProviderSettingsRequest(string? ConfigJson, string? Secret, bool IsEnabled);

/// <summary>OAuth applications (Admin+ management), the /oauth/* provider endpoints, and third-party
/// provider settings.</summary>
public static class OAuthEndpoints
{
    public static void MapOAuthManagementEndpoints(this RouteGroupBuilder api)
    {
        var apps = api.MapGroup("/oauth-applications").RequireAuthorization();

        apps.MapGet("/", async (OAuthApplicationService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        apps.MapPost("/", async (CreateOAuthApplicationRequest r, OAuthApplicationService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateAsync(new CreateOAuthApplicationCommand(r.Name, r.RedirectUris, r.AllowedScopes), ct);
            return Results.Created($"/api/v1/oauth-applications/{dto.Id}", dto);
        });

        apps.MapDelete("/{id:guid}", async (Guid id, OAuthApplicationService svc, CancellationToken ct) =>
        {
            await svc.RevokeAsync(id, ct);
            return Results.NoContent();
        });

        // ---- Provider settings (Slack/GitHub/... — Admin+) ----
        var providers = api.MapGroup("/integrations/providers").RequireAuthorization();

        providers.MapGet("/", async (IntegrationProviderSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        providers.MapGet("/{provider}", async (string provider, IntegrationProviderSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(provider, ct)));

        providers.MapPut("/{provider}", async (string provider, UpdateProviderSettingsRequest r, IntegrationProviderSettingsService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(provider, new UpdateIntegrationProviderSettingsCommand(r.ConfigJson ?? "{}", r.Secret, r.IsEnabled), ct)));

        providers.MapPost("/slack/test", async (string? message, ISlackClient slack, Planvexa.BuildingBlocks.Workspaces.IWorkspaceContextAccessor ws, CancellationToken ct) =>
        {
            var result = await slack.PostMessageAsync(ws.Current.WorkspaceId, string.IsNullOrWhiteSpace(message) ? "Test message from Planvexa." : message, ct);
            return Results.Ok(new { result.Success, result.Detail });
        });

        providers.MapPost("/github/test", async (int issueNumber, string? message, IGitHubClient github, Planvexa.BuildingBlocks.Workspaces.IWorkspaceContextAccessor ws, CancellationToken ct) =>
        {
            var result = await github.CreateIssueCommentAsync(ws.Current.WorkspaceId, issueNumber, string.IsNullOrWhiteSpace(message) ? "Test comment from Planvexa." : message, ct);
            return Results.Ok(new { result.Success, result.Detail });
        });
    }

    /// <summary>The OAuth2 provider endpoints themselves, deliberately NOT under /api/v1 (standard OAuth
    /// convention — third-party OAuth libraries expect /oauth/authorize and /oauth/token at a stable,
    /// version-independent path).</summary>
    public static void MapOAuthProviderEndpoints(this IEndpointRouteBuilder app)
    {
        var oauth = app.MapGroup("/oauth");

        // Called by an already-authenticated Planvexa user's own session (JWT/dev-auth/PAT — whichever
        // the normal pipeline already resolved) approving an app's requested scopes for their current
        // workspace. Never reachable by an OAuth-token-authenticated request itself (no scope metadata),
        // which is correct: minting a NEW authorization requires a real user session, not an existing token.
        oauth.MapPost("/authorize", async (OAuthAuthorizeRequest r, OAuthAuthorizationService svc, CancellationToken ct) =>
        {
            var result = await svc.AuthorizeAsync(new OAuthAuthorizeCommand(r.ClientId, r.RedirectUri, r.Scope), ct);
            return Results.Ok(result);
        }).RequireAuthorization();

        // Called by the third-party app's own backend (no Planvexa user session) — authenticated solely by
        // client_id/client_secret, exactly like OAuth2's confidential-client token endpoint.
        oauth.MapPost("/token", async (OAuthTokenRequest r, OAuthAuthorizationService svc, CancellationToken ct) =>
        {
            var result = r.GrantType switch
            {
                "authorization_code" => await svc.ExchangeAuthorizationCodeAsync(r.ClientId, r.ClientSecret, r.Code ?? string.Empty, r.RedirectUri ?? string.Empty, ct),
                "refresh_token" => await svc.RefreshAsync(r.ClientId, r.ClientSecret, r.RefreshToken ?? string.Empty, ct),
                _ => throw new Planvexa.BuildingBlocks.Exceptions.ValidationAppException("unsupported_grant_type"),
            };
            return Results.Ok(result);
        }).AllowAnonymous();
    }
}
