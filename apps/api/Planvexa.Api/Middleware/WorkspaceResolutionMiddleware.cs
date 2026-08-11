namespace Planvexa.Api.Middleware;

using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Tenancy.Application;

/// <summary>
/// Resolves the Workspace for the request from the <c>X-Workspace</c> header — the sole selector
/// (AGENTS.md: "There is no Organization/Tenant layer"; Workspace is the single top-level
/// business/authorization boundary). The resolved <see cref="WorkspaceContext"/> is immutable and is
/// NEVER taken from a request body (AGENTS.md rule 5).
/// </summary>
public sealed class WorkspaceResolutionMiddleware(RequestDelegate next)
{
    public const string WorkspaceHeader = "X-Workspace";
    public const string CorrelationHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationHeader].ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.CreateVersion7().ToString();
        }

        context.Response.Headers[CorrelationHeader] = correlationId;

        var currentUser = context.RequestServices.GetRequiredService<CurrentUser>();
        var workspaceHeader = context.Request.Headers[WorkspaceHeader].ToString();
        var hasWorkspaceHeader = !string.IsNullOrWhiteSpace(workspaceHeader);

        if (hasWorkspaceHeader)
        {
            if (!Guid.TryParse(workspaceHeader, out var workspaceId))
            {
                await WriteForbiddenWorkspaceAsync(context);
                return;
            }

            if (currentUser.IsAuthenticated)
            {
                var resolver = context.RequestServices.GetRequiredService<IWorkspaceResolver>();
                var resolution = await resolver.ResolveByWorkspaceIdAsync(workspaceId, currentUser.UserId, context.RequestAborted);
                if (resolution is null)
                {
                    await WriteForbiddenWorkspaceAsync(context);
                    return;
                }

                // Spec section 44 / AGENTS.md Phase 1B: a Workspace with MfaRequired must actually block
                // access for a member who hasn't completed a second factor — not merely hide a UI option.
                // Checked here, before the WorkspaceContext is ever set, so nothing downstream (including
                // the bootstrap GET /workspaces/me listing) can be reached under this Workspace without it.
                if (resolution.RequiresMfa && !currentUser.HasVerifiedMfa)
                {
                    await WriteMfaRequiredAsync(context);
                    return;
                }

                var accessor = context.RequestServices.GetRequiredService<IWorkspaceContextAccessor>();
                accessor.Set(new WorkspaceContext(
                    workspaceId: resolution.WorkspaceId,
                    userId: currentUser.UserId,
                    membershipId: null,
                    role: resolution.Role.ToString(),
                    permissions: new HashSet<string>(),
                    entitlements: resolution.EnabledFeatures,
                    correlationId: correlationId));
            }
        }

        await next(context);
    }

    private static async Task WriteForbiddenWorkspaceAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.io/403",
            title = "Forbidden",
            status = 403,
            detail = "The requested workspace does not exist, or the current user does not have access to it.",
        });
    }

    /// <summary>A distinct problem "type" from the generic Forbidden above so the web client can tell
    /// "you're not a member" apart from "you're a member, but this workspace requires MFA" and route the
    /// user to complete MFA instead of a plain access-denied page.</summary>
    private static async Task WriteMfaRequiredAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://planvexa.dev/problems/mfa-required",
            title = "Multi-factor authentication required",
            status = 403,
            detail = "This workspace requires multi-factor authentication. Complete MFA setup and sign in again to continue.",
        });
    }
}
