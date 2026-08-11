namespace Planvexa.Api.Middleware;

using System.Security.Claims;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.SharedContracts.Integrations;

/// <summary>
/// Authenticates requests bearing an OAuth2 access token (<c>Authorization: Bearer oat_...</c>) when no
/// other authentication succeeded — the OAuth-token sibling of <see cref="PatAuthenticationMiddleware"/>,
/// same shape and same reasoning (Workspace is NEVER taken from the request body/header unless it agrees
/// with the token's own workspace, AGENTS.md rule 5). The distinguishing detail: this sets a "scope"
/// claim per granted scope, which <see cref="OAuthScopeEnforcementMiddleware"/> reads to enforce the new
/// OAuth privilege boundary — an OAuth-token-authenticated request may ONLY reach an endpoint that
/// explicitly opted in with <c>.RequireOAuthScope(...)</c> and only when the token was granted that scope.
/// </summary>
public sealed class OAuthAuthenticationMiddleware(RequestDelegate next)
{
    public const string Scheme = "OAuth";
    private const string BearerPrefix = "Bearer ";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true && TryGetOAuthToken(context, out var rawToken))
        {
            var verifier = context.RequestServices.GetRequiredService<IOAuthTokenVerifier>();
            var principal = await verifier.VerifyAsync(rawToken, context.RequestAborted);
            if (principal is not null)
            {
                var claims = new List<Claim>
                {
                    new("sub", principal.Subject),
                    new(ClaimTypes.NameIdentifier, principal.Subject),
                    new("email", principal.Email),
                    new(ClaimTypes.Email, principal.Email),
                    new("name", principal.DisplayName),
                    new(ClaimTypes.Name, principal.DisplayName),
                };
                claims.AddRange(principal.Scopes.Select(scope => new Claim("scope", scope)));
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));

                var workspaceHeader = context.Request.Headers["X-Workspace"].ToString();
                if (!string.IsNullOrWhiteSpace(workspaceHeader)
                    && (!Guid.TryParse(workspaceHeader, out var headerWorkspaceId) || headerWorkspaceId != principal.WorkspaceId))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                var accessor = context.RequestServices.GetRequiredService<IWorkspaceContextAccessor>();
                accessor.Set(new WorkspaceContext(
                    workspaceId: principal.WorkspaceId,
                    userId: principal.UserId,
                    membershipId: null,
                    role: string.Empty,
                    permissions: new HashSet<string>(),
                    entitlements: new HashSet<string>(),
                    correlationId: Guid.CreateVersion7().ToString()));
            }
        }

        await next(context);
    }

    private static bool TryGetOAuthToken(HttpContext context, out string rawToken)
    {
        rawToken = string.Empty;
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = header[BearerPrefix.Length..].Trim();
        if (!value.StartsWith("oat_", StringComparison.Ordinal))
        {
            return false;
        }

        rawToken = value;
        return true;
    }
}

/// <summary>Endpoint metadata: the OAuth scope required to reach this endpoint with an OAuth-token
/// principal. Endpoints with no such metadata are unreachable via an OAuth token (default-deny — see
/// <see cref="OAuthScopeEnforcementMiddleware"/>), only via a normal user session or PAT.</summary>
public sealed record RequiresOAuthScopeMetadata(string Scope);

public static class OAuthScopeEndpointExtensions
{
    public static RouteHandlerBuilder RequireOAuthScope(this RouteHandlerBuilder builder, string scope)
        => builder.WithMetadata(new RequiresOAuthScopeMetadata(scope));
}

/// <summary>
/// Enforces the OAuth scope boundary: a request authenticated via <see cref="OAuthAuthenticationMiddleware"/>
/// may only reach an endpoint carrying <see cref="RequiresOAuthScopeMetadata"/> for a scope the token was
/// granted. Default-deny (no metadata => 403) rather than default-allow — OAuth applications are a NEW
/// privilege boundary (security brief), so the safe default is "an OAuth token can do
/// nothing until an endpoint explicitly opts in", not "everything except what's blocked". Runs after
/// routing has matched an endpoint (registered after <c>UseAuthorization</c> in Program.cs, which already
/// depends on endpoint metadata being available). Never touches PAT/JWT-authenticated requests — those
/// keep their existing (unscoped) behavior.
/// </summary>
public sealed class OAuthScopeEnforcementMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var isOAuthAuthenticated = string.Equals(
            context.User?.Identity?.AuthenticationType, OAuthAuthenticationMiddleware.Scheme, StringComparison.Ordinal);

        if (isOAuthAuthenticated)
        {
            var required = context.GetEndpoint()?.Metadata.GetMetadata<RequiresOAuthScopeMetadata>();
            var grantedScopes = context.User!.FindAll("scope").Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
            if (required is null || !grantedScopes.Contains(required.Scope))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await next(context);
    }
}
