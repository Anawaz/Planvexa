namespace Planvexa.Api.Middleware;

using System.Security.Claims;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.SharedContracts.Integrations;

/// <summary>
/// Authenticates requests bearing a personal access token (<c>Authorization: Bearer pat_...</c>) when no
/// other authentication succeeded. The token is verified by the Integrations module, which returns the
/// owning user + the Workspace the token was minted for; this middleware then sets an authenticated
/// principal and binds the ambient Workspace context to it. Runs before user/workspace resolution so
/// the standard downstream middleware sees an authenticated principal. Workspace is NEVER taken from
/// the request body — it comes from the token (AGENTS.md rule 5). An optional X-Workspace header is
/// still honored, but only if it agrees with the token's own workspace.
/// </summary>
public sealed class PatAuthenticationMiddleware(RequestDelegate next)
{
    public const string Scheme = "Pat";
    private const string BearerPrefix = "Bearer ";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true && TryGetPat(context, out var rawToken))
        {
            var verifier = context.RequestServices.GetRequiredService<IAccessTokenVerifier>();
            var principal = await verifier.VerifyAsync(rawToken, context.RequestAborted);
            if (principal is not null)
            {
                var claims = new[]
                {
                    new Claim("sub", principal.Subject),
                    new Claim(ClaimTypes.NameIdentifier, principal.Subject),
                    new Claim("email", principal.Email),
                    new Claim(ClaimTypes.Email, principal.Email),
                    new Claim("name", principal.DisplayName),
                    new Claim(ClaimTypes.Name, principal.DisplayName),
                };
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

    private static bool TryGetPat(HttpContext context, out string rawToken)
    {
        rawToken = string.Empty;
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = header[BearerPrefix.Length..].Trim();
        if (!value.StartsWith("pat_", StringComparison.Ordinal))
        {
            return false;
        }

        rawToken = value;
        return true;
    }
}
