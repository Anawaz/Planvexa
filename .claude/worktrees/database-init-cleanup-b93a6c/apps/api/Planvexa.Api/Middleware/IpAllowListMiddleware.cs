namespace Planvexa.Api.Middleware;

using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Governance.Application.Services;

/// <summary>
/// Enforces a workspace's IP allow list. Runs after <see
/// cref="WorkspaceResolutionMiddleware"/> so <see cref="IWorkspaceContextAccessor"/> already reflects the
/// resolved workspace for this request. Requests with no resolved workspace (bootstrap endpoints,
/// anonymous public routes) are not checked here — those either have no workspace-scoped data to protect
/// or run their own access control (e.g. public form tokens). A workspace with no configured rules is
/// unrestricted, so this is a no-op (one cheap query) for every workspace that hasn't opted in.
///
/// The allow-list management endpoints themselves (<c>/api/v1/governance/ip-allow-rules</c>, already
/// gated to workspace Admin+ by <c>GovernanceAuthorizer</c>) are exempt from this check — otherwise an
/// Admin who adds an overly-narrow rule (or connects from a new network before updating it) would lock
/// themselves out permanently with no way to call the one endpoint that could fix it.
/// </summary>
public sealed class IpAllowListMiddleware(RequestDelegate next)
{
    private const string ManagementPathPrefix = "/api/v1/governance/ip-allow-rules";

    public async Task InvokeAsync(HttpContext context)
    {
        var accessor = context.RequestServices.GetRequiredService<IWorkspaceContextAccessor>();
        var workspace = accessor.Current;

        if (workspace.HasWorkspace && !context.Request.Path.StartsWithSegments(ManagementPathPrefix))
        {
            var remoteAddress = context.Connection.RemoteIpAddress;
            if (remoteAddress is not null)
            {
                var service = context.RequestServices.GetRequiredService<IpAllowListService>();
                var allowed = await service.IsAllowedAsync(workspace.WorkspaceId, remoteAddress, context.RequestAborted);
                if (!allowed)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://httpstatuses.io/403",
                        title = "Forbidden",
                        status = 403,
                        detail = "Your network is not on this workspace's allowed IP list.",
                    });
                    return;
                }
            }
        }

        await next(context);
    }
}
