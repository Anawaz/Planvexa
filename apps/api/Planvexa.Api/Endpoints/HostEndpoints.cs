namespace Planvexa.Api.Endpoints;

using FluentValidation;
using Planvexa.Api.Auth;
using Planvexa.Api.Platform;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.Infrastructure.HostAdmin;
using Planvexa.Infrastructure.Platform;
using Planvexa.SharedContracts.Users;

public sealed record DeleteWorkspaceAsHostRequest(string ConfirmSlug);
public sealed record SetHostAdminRequest(bool Granted);

/// <summary>
/// Partial update: every field is optional and null means "leave it alone", so the console's separate
/// access and branding forms can each submit only their own fields.
/// </summary>
public sealed record UpdateInstanceSettingsRequest(
    bool? AllowSelfRegistration,
    string? WorkspaceCreationPolicy,
    string? InstanceName,
    string? LogoUrl,
    string? SupportEmail);

public sealed class UpdateInstanceSettingsRequestValidator : AbstractValidator<UpdateInstanceSettingsRequest>
{
    public UpdateInstanceSettingsRequestValidator()
    {
        // Only two policies exist, and silently coercing an unknown one (which is what
        // WorkspaceCreationPolicies.Normalize does as a last-resort defence) would let a typo read back
        // as "Anyone" and look like the save worked.
        RuleFor(r => r.WorkspaceCreationPolicy)
            .Must(value => value is null
                || value == WorkspaceCreationPolicies.Anyone
                || value == WorkspaceCreationPolicies.HostAdminsOnly)
            .WithMessage($"Workspace creation policy must be '{WorkspaceCreationPolicies.Anyone}' or '{WorkspaceCreationPolicies.HostAdminsOnly}'.");

        RuleFor(r => r.InstanceName).MaximumLength(200);
        RuleFor(r => r.LogoUrl).MaximumLength(500);
        RuleFor(r => r.SupportEmail).MaximumLength(320);

        RuleFor(r => r.SupportEmail)
            .EmailAddress()
            .When(r => !string.IsNullOrWhiteSpace(r.SupportEmail));

        // The logo is rendered in an <img src> on the anonymous sign-in page; restricting it to
        // absolute http(s) keeps a javascript:/data: URL from being stored and served there.
        RuleFor(r => r.LogoUrl)
            .Must(value => Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Logo URL must be an absolute http(s) URL.")
            .When(r => !string.IsNullOrWhiteSpace(r.LogoUrl));
    }
}

/// <summary>
/// Host administration — instance-level management of this Planvexa installation, for whoever runs
/// the server. Separate from Workspace administration (<see cref="GovernanceEndpoints"/> and the
/// members/roles endpoints in <see cref="ApiEndpoints"/>), which stays scoped to a single Workspace.
///
/// Every endpoint under <c>/host</c> runs with NO ambient Workspace: the web client omits
/// <c>X-Workspace</c> for these calls, so <see cref="Middleware.WorkspaceResolutionMiddleware"/>
/// leaves the context empty and the host-admin RLS policies (script 0094) are what authorize the
/// cross-Workspace reads. Authorization is the <see cref="HostAdminRequirement.PolicyName"/> policy,
/// never a Workspace role.
///
/// Scope boundary, deliberately: metadata and aggregates only. There is no endpoint here that returns
/// task titles, document bodies or messages, and none should be added — Workspace stays the isolation
/// boundary for content.
/// </summary>
public static class HostEndpoints
{
    public static void MapHostEndpoints(this RouteGroupBuilder api)
    {
        MapHostAdminProbe(api);

        var host = api.MapGroup("/host").RequireAuthorization(HostAdminRequirement.PolicyName);

        MapOverview(host);
        MapWorkspaces(host);
        MapUsers(host);
        MapActivity(host);
        MapSettings(host);
        MapHealthAndLogs(host);
    }

    /// <summary>
    /// "Am I a host administrator?" — authenticated but deliberately NOT behind the host-admin policy,
    /// so the ordinary shell can ask the question and get <c>false</c> instead of a 403 it would have
    /// to swallow. Used by the Topbar (whether to offer the console at all) and by the /host layout's
    /// own gate. The real enforcement is the policy on the /host group; this only decides what to
    /// render.
    /// </summary>
    private static void MapHostAdminProbe(RouteGroupBuilder api)
    {
        api.MapGet("/users/me/host-admin", async (ICurrentUser user, IUserDirectory users, CancellationToken ct) =>
                Results.Ok(new { isHostAdmin = await users.IsHostAdminAsync(user.UserId, ct) }))
            .RequireAuthorization()
            .WithName("GetCurrentUserHostAdmin");
    }

    private static void MapOverview(RouteGroupBuilder host)
    {
        host.MapGet("/overview", async (HostAdminQueries queries, CancellationToken ct) =>
                Results.Ok(await queries.GetOverviewAsync(ct)))
            .WithName("GetHostOverview");
    }

    private static void MapWorkspaces(RouteGroupBuilder host)
    {
        var group = host.MapGroup("/workspaces");

        group.MapGet("/", async (
                string? search, string? status, int? skip, int? take,
                HostAdminQueries queries, CancellationToken ct) =>
                Results.Ok(await queries.ListWorkspacesAsync(search, status, skip ?? 0, take ?? 50, ct)))
            .WithName("ListHostWorkspaces");

        group.MapGet("/{workspaceId:guid}", async (Guid workspaceId, HostAdminQueries queries, CancellationToken ct) =>
                await queries.GetWorkspaceAsync(workspaceId, ct) is { } detail
                    ? Results.Ok(detail)
                    : Results.NotFound())
            .WithName("GetHostWorkspace");

        // Separate from the detail endpoint because it costs a stamped round-trip into the target
        // workspace (see HostAdminQueries.GetWorkspaceUsageAsync) — the list view never pays for it.
        group.MapGet("/{workspaceId:guid}/usage", async (Guid workspaceId, HostAdminQueries queries, CancellationToken ct) =>
                Results.Ok(await queries.GetWorkspaceUsageAsync(workspaceId, ct)))
            .WithName("GetHostWorkspaceUsage");

        group.MapPost("/{workspaceId:guid}/suspend", async (
                Guid workspaceId, HostAdminActionService actions, CancellationToken ct) =>
                Results.Ok(new { status = await actions.SuspendWorkspaceAsync(workspaceId, ct) }))
            .WithName("SuspendHostWorkspace");

        group.MapPost("/{workspaceId:guid}/restore", async (
                Guid workspaceId, HostAdminActionService actions, CancellationToken ct) =>
                Results.Ok(new { status = await actions.RestoreWorkspaceAsync(workspaceId, ct) }))
            .WithName("RestoreHostWorkspace");

        // POST, not DELETE: irreversible and needs a confirmation body, matching the Owner-facing
        // /workspaces/{id}/delete it shares an implementation with.
        group.MapPost("/{workspaceId:guid}/delete", async (
                Guid workspaceId, DeleteWorkspaceAsHostRequest request,
                HostAdminActionService actions, CancellationToken ct) =>
            {
                await actions.DeleteWorkspaceAsync(workspaceId, request.ConfirmSlug, ct);
                return Results.NoContent();
            })
            .WithName("DeleteHostWorkspace");
    }

    private static void MapUsers(RouteGroupBuilder host)
    {
        var group = host.MapGroup("/users");

        group.MapGet("/", async (
                string? search, string? status, int? skip, int? take,
                HostAdminQueries queries, CancellationToken ct) =>
                Results.Ok(await queries.ListUsersAsync(search, status, skip ?? 0, take ?? 50, ct)))
            .WithName("ListHostUsers");

        group.MapGet("/{userId:guid}", async (Guid userId, HostAdminQueries queries, CancellationToken ct) =>
                await queries.GetUserAsync(userId, ct) is { } detail
                    ? Results.Ok(detail)
                    : Results.NotFound())
            .WithName("GetHostUser");

        group.MapPost("/{userId:guid}/disable", async (
                Guid userId, HostAdminActionService actions, CancellationToken ct) =>
            {
                await actions.SetUserActiveAsync(userId, active: false, ct);
                return Results.NoContent();
            })
            .WithName("DisableHostUser");

        group.MapPost("/{userId:guid}/enable", async (
                Guid userId, HostAdminActionService actions, CancellationToken ct) =>
            {
                await actions.SetUserActiveAsync(userId, active: true, ct);
                return Results.NoContent();
            })
            .WithName("EnableHostUser");

        group.MapPost("/{userId:guid}/host-admin", async (
                Guid userId, SetHostAdminRequest request, HostAdminActionService actions, CancellationToken ct) =>
            {
                await actions.SetHostAdminAsync(userId, request.Granted, ct);
                return Results.NoContent();
            })
            .WithName("SetHostUserHostAdmin");
    }

    private static void MapHealthAndLogs(RouteGroupBuilder host)
    {
        // Distinct from the anonymous /health/live and /health/ready probes, which stay minimal and
        // load-balancer-facing. This one answers "what should the operator look at?" and is therefore
        // host-admin-only — it reports versions, backlogs and configuration.
        host.MapGet("/health", async (InstanceHealthService health, CancellationToken ct) =>
                Results.Ok(await health.GetAsync(ct)))
            .WithName("GetInstanceHealth");

        host.MapGet("/logs", async (
                string? level, string? category, string? search,
                DateTimeOffset? from, DateTimeOffset? to, int? skip, int? take,
                InstanceLogQueries logs, CancellationToken ct) =>
                Results.Ok(await logs.SearchAsync(level, category, search, from, to, skip ?? 0, take ?? 100, ct)))
            .WithName("ListInstanceLogs");
    }

    private static void MapSettings(RouteGroupBuilder host)
    {
        host.MapGet("/settings", async (InstanceSettingsService settings, CancellationToken ct) =>
                Results.Ok(ToResponse(await settings.GetForAdminAsync(ct))))
            .WithName("GetInstanceSettings");

        host.MapPut("/settings", async (
                UpdateInstanceSettingsRequest request, InstanceSettingsService settings,
                IAuditWriter audit, IUnitOfWork unitOfWork, CancellationToken ct) =>
            {
                var updated = await settings.UpdateAsync(
                    new UpdateInstanceSettingsCommand(
                        request.AllowSelfRegistration, request.WorkspaceCreationPolicy,
                        request.InstanceName, request.LogoUrl, request.SupportEmail),
                    ct);

                // Audited with the resulting values rather than a diff: these are five non-secret
                // settings, so the new state IS the useful record and reconstructing history from a
                // sequence of them needs no extra bookkeeping. Runs with no ambient workspace, so this
                // lands as a platform-level event (WorkspaceId null).
                audit.Write("host.settings.updated", nameof(InstanceSettings), null, ToResponse(updated));
                await unitOfWork.SaveChangesAsync(ct);

                return Results.Ok(ToResponse(updated));
            })
            .AddEndpointFilter<ValidationFilter<UpdateInstanceSettingsRequest>>()
            .WithName("UpdateInstanceSettings");
    }

    private static object ToResponse(InstanceSettings settings) => new
    {
        settings.AllowSelfRegistration,
        settings.WorkspaceCreationPolicy,
        settings.InstanceName,
        settings.LogoUrl,
        settings.SupportEmail,
        settings.UpdatedAtUtc,
        settings.UpdatedByUserId,
    };

    private static void MapActivity(RouteGroupBuilder host)
    {
        // The instance-wide audit trail: every workspace's events plus the platform-level ones
        // (WorkspaceId null), which is what host actions themselves are recorded as.
        host.MapGet("/activity", async (
                string? action, string? entityType, Guid? actorUserId, Guid? workspaceId,
                DateTimeOffset? from, DateTimeOffset? to, int? skip, int? take,
                HostAdminQueries queries, CancellationToken ct) =>
                Results.Ok(await queries.ListActivityAsync(
                    action, entityType, actorUserId, workspaceId, from, to, skip ?? 0, take ?? 50, ct)))
            .WithName("ListHostActivity");

        // Reached by a plain <a href> download, which cannot set headers — but unlike the Workspace
        // exports it needs no x-workspace query alias, because a host request deliberately carries no
        // workspace at all. The session cookie on the BFF proxy is what authenticates it.
        host.MapGet("/activity/export", async (
                string? action, string? entityType, Guid? actorUserId, Guid? workspaceId,
                DateTimeOffset? from, DateTimeOffset? to, HostAdminQueries queries, CancellationToken ct) =>
            {
                var csv = await queries.ExportActivityCsvAsync(action, entityType, actorUserId, workspaceId, from, to, ct);
                return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "instance-activity.csv");
            })
            .WithName("ExportHostActivity");
    }
}
