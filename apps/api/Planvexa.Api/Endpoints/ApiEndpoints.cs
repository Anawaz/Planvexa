namespace Planvexa.Api.Endpoints;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.Identity.Application.Services;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Domain;
using Planvexa.SharedContracts.Users;

public static class ApiEndpoints
{
    public static void MapPlanvexaEndpoints(this IEndpointRouteBuilder app)
    {
        MapHealth(app);

        var api = app.MapGroup("/api/v1");

        MapPublicConfig(api);
        MapUsers(api);
        MapWorkspaces(api);
        MapInvitations(api);
        MapTeams(api);
        MapFeatures(api);
        MapRoles(api);

        // Core Work Management.
        api.MapWorkStructureEndpoints();
        api.MapWorkTaskEndpoints();
        api.MapAttachmentEndpoints();

        // Work hierarchy completeness: folder nesting/duplicate/templates/favourites/default views.
        api.MapWorkExtrasEndpoints();

        // ADR-0003 — per-resource ACL (private spaces/folders/lists/tasks, sharing).
        api.MapResourceSharingEndpoints();

        // Collaboration, Notifications & Sharing.
        api.MapCollaborationEndpoints();

        // Time Tracking & Timesheets.
        api.MapTimeTrackingEndpoints();

        // Advanced Views, Planning & Reporting.
        api.MapPlanningEndpoints();

        // Documents, Forms, Automations & Integrations.
        api.MapDocumentEndpoints();
        api.MapFormEndpoints();
        api.MapAutomationEndpoints();

        // Enterprise Security & Governance.
        api.MapGovernanceEndpoints();

        // AI, Mobile & Data Retention.
        api.MapAiMobileEndpoints();

        // Chat — workspace channels & messages (realtime).
        api.MapChatEndpoints();

        // Global search ("search or jump to").
        api.MapSearchEndpoints();

        // Goals/OKRs & reporting completeness.
        api.MapGoalEndpoints();
        api.MapReportingExtraEndpoints();

        // OAuth applications, third-party provider settings & data importers.
        api.MapOAuthManagementEndpoints();
        api.MapImportEndpoints();
        app.MapOAuthProviderEndpoints();

        // Whiteboards & Clips.
        api.MapWhiteboardEndpoints();
        api.MapClipEndpoints();

        // Item 6 — local-disk signed upload/download URL proxy (no-op when FileStorage:Provider
        // is S3, since S3 presigned URLs point directly at the object store).
        app.MapSignedFileEndpoints();
    }

    private static void MapHealth(IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .AllowAnonymous()
            .WithName("HealthLive");

        app.MapGet("/health/ready", async (PlanvexaDbContext db, CancellationToken ct) =>
            {
                var canConnect = await db.Database.CanConnectAsync(ct);
                return canConnect
                    ? Results.Ok(new { status = "ready" })
                    : Results.Json(new { status = "not-ready" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .AllowAnonymous()
            .WithName("HealthReady");
    }

    private static void MapPublicConfig(RouteGroupBuilder api)
    {
        // Anonymous: the landing page reads this before the visitor has a session, to decide whether
        // to show Sign up / Start onboarding at all (see Registration:AllowSelfRegistration and
        // UserDirectory.GetOrProvisionAsync's gate).
        api.MapGet("/public/registration-policy", (IConfiguration configuration) =>
            {
                var allowSelfRegistration = !bool.TryParse(configuration["Registration:AllowSelfRegistration"], out var configured) || configured;
                return Results.Ok(new { allowSelfRegistration });
            })
            .AllowAnonymous()
            .WithName("GetRegistrationPolicy");
    }

    private static void MapUsers(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/users").RequireAuthorization();

        // Bootstrap endpoint (ADR 0015): returns the authenticated user's internal global UserId
        // directly, so the frontend never resolves the current user by matching an email.
        group.MapGet("/me", async (ICurrentUser user, IUserDirectory directory, CancellationToken ct) =>
            {
                var info = await directory.FindByIdAsync(user.UserId, ct);
                return Results.Ok(new UserInfo(
                    user.UserId,
                    info?.Email ?? user.Email,
                    info?.DisplayName ?? user.DisplayName,
                    info?.AvatarUrl,
                    info?.Timezone,
                    info?.Locale,
                    info?.Theme));
            })
            .WithName("GetCurrentUser");

        // GDPR-style user-data export/deletion. Self-service only, no workspaceId
        // route parameter — the service always acts on the caller's own ICurrentUser.UserId, so (like
        // GetCurrentUser above) these run with no Workspace selected. See UserDataService's doc comment
        // for why self-service (not a Workspace-Owner-on-behalf-of-a-member model) was chosen.
        group.MapGet("/me/export", async (UserDataService svc, CancellationToken ct) =>
            {
                var zip = await svc.ExportAsync(ct);
                return Results.File(zip, "application/zip", "user-data-export.zip");
            })
            .WithName("ExportMyUserData");

        group.MapDelete("/me", async (UserDataService svc, CancellationToken ct) =>
                Results.Ok(await svc.DeleteAsync(ct)))
            .WithName("DeleteMyAccount");

        // Self-service profile edit (account-level, not Workspace-level — same no-Workspace-required
        // shape as GetCurrentUser above). Only DisplayName is editable here; Email is IdP-owned.
        group.MapPatch("/me", async (UpdateDisplayNameRequest request, UserDataService svc, CancellationToken ct) =>
                Results.Ok(await svc.UpdateDisplayNameAsync(request.DisplayName, request.Timezone, request.Locale, request.Theme, ct)))
            .AddEndpointFilter<ValidationFilter<UpdateDisplayNameRequest>>()
            .WithName("UpdateCurrentUser");

        // Self-service avatar upload — see AvatarService for the storage/malware-scan pipeline. Served
        // by plain bearer-authenticated streaming (no signed URLs), same convention as
        // DocumentEndpoints' inline images.
        group.MapPost("/me/avatar", async (IFormFile file, AvatarService svc, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return Results.Ok(await svc.UploadAsync(file.ContentType, file.Length, content, ct));
            })
            .DisableAntiforgery()
            .WithName("UploadCurrentUserAvatar");

        group.MapGet("/{userId:guid}/avatar", async (Guid userId, AvatarService svc, CancellationToken ct) =>
                Results.Stream(await svc.DownloadAsync(userId, ct), "application/octet-stream"))
            .WithName("GetUserAvatar");
    }

    private static void MapWorkspaces(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/workspaces").RequireAuthorization();

        group.MapPost("/", async (
                CreateWorkspaceRequest request,
                ICurrentUser user,
                WorkspaceRegistrationService onboarding,
                CancellationToken ct) =>
            {
                // Always a brand-new, independent Workspace whose creator becomes Owner (ADR 0015) — the
                // only workspace-creation path, whether this is the caller's first Workspace or another
                // one added to their account. Returns a Workspace, never an Organization.
                var dto = await onboarding.OnboardWorkspaceAsync(request.Name, user.UserId, request.Slug, ct);
                return Results.Created($"/api/v1/workspaces/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateWorkspaceRequest>>()
            .WithName("CreateWorkspace");

        // Workspaces the caller is actually a member of — the shell picks its default from here,
        // because the workspace selector only succeeds for accessible workspaces.
        group.MapGet("/mine", async (
                ICurrentUser user, WorkspaceService service, CancellationToken ct) =>
                Results.Ok(await service.ListForUserAsync(user.UserId, ct)))
            .WithName("ListMyWorkspaces");

        // Bootstrap endpoint (ADR 0015): canonical name for the caller's Workspace memberships,
        // usable before a Workspace is selected. Alias of /mine.
        group.MapGet("/me", async (
                ICurrentUser user, WorkspaceService service, CancellationToken ct) =>
                Results.Ok(await service.ListForUserAsync(user.UserId, ct)))
            .WithName("ListMyWorkspaceMemberships");

        group.MapPost("/{workspaceId:guid}/invitations", async (
                Guid workspaceId,
                InviteMemberRequest request,
                InvitationService service,
                CancellationToken ct) =>
            {
                var role = Enum.Parse<MembershipRole>(request.Role, ignoreCase: true);
                var dto = await service.InviteAsync(new InviteMemberCommand(workspaceId, request.Email, role), ct);
                return Results.Created($"/api/v1/invitations/{dto.InvitationId}", dto);
            })
            .AddEndpointFilter<ValidationFilter<InviteMemberRequest>>()
            .WithName("InviteMember");

        group.MapGet("/{workspaceId:guid}/members", async (
                Guid workspaceId, MembershipService service, IUserDirectory users, CancellationToken ct) =>
            {
                var members = await service.ListWorkspaceMembersAsync(workspaceId, ct);
                // ponytail: N+1 lookups; batch when member lists grow.
                var enriched = new List<object>(members.Count);
                foreach (var member in members)
                {
                    var info = await users.FindByIdAsync(member.UserId, ct);
                    enriched.Add(new
                    {
                        member.Id,
                        member.UserId,
                        member.Role,
                        member.Status,
                        member.IsGuest,
                        member.JoinedAtUtc,
                        DisplayName = info?.DisplayName,
                        Email = info?.Email,
                        AvatarUrl = info?.AvatarUrl,
                    });
                }

                return Results.Ok(enriched);
            })
            .WithName("ListWorkspaceMembers");

        // Pending invitations for the members admin screen — never returns raw tokens.
        group.MapGet("/{workspaceId:guid}/invitations", async (
                Guid workspaceId, InvitationService service, CancellationToken ct) =>
                Results.Ok(await service.ListPendingAsync(workspaceId, ct)))
            .WithName("ListPendingInvitations");

        group.MapPost("/{workspaceId:guid}/invitations/{invitationId:guid}/resend", async (
                Guid workspaceId, Guid invitationId, InvitationService service, CancellationToken ct) =>
                Results.Ok(await service.ResendAsync(workspaceId, invitationId, ct)))
            .WithName("ResendInvitation");

        group.MapPost("/{workspaceId:guid}/invitations/{invitationId:guid}/revoke", async (
                Guid workspaceId, Guid invitationId, InvitationService service, CancellationToken ct) =>
            {
                await service.RevokeAsync(workspaceId, invitationId, ct);
                return Results.NoContent();
            })
            .WithName("RevokeInvitation");

        group.MapPatch("/{workspaceId:guid}/members/{membershipId:guid}", async (
                Guid workspaceId, Guid membershipId, ChangeMemberRoleRequest request,
                MembershipService service, CancellationToken ct) =>
            {
                var role = Enum.Parse<MembershipRole>(request.Role, ignoreCase: true);
                var dto = await service.ChangeRoleAsync(new ChangeMemberRoleCommand(workspaceId, membershipId, role), ct);
                return Results.Ok(dto);
            })
            .AddEndpointFilter<ValidationFilter<ChangeMemberRoleRequest>>()
            .WithName("ChangeMemberRole");

        group.MapPost("/{workspaceId:guid}/members/{membershipId:guid}/deactivate", async (
                Guid workspaceId, Guid membershipId, MembershipService service, CancellationToken ct) =>
                Results.Ok(await service.DeactivateAsync(workspaceId, membershipId, ct)))
            .WithName("DeactivateMember");

        group.MapPost("/{workspaceId:guid}/members/{membershipId:guid}/reactivate", async (
                Guid workspaceId, Guid membershipId, MembershipService service, CancellationToken ct) =>
                Results.Ok(await service.ReactivateAsync(workspaceId, membershipId, ct)))
            .WithName("ReactivateMember");

        group.MapPost("/{workspaceId:guid}/leave", async (
                Guid workspaceId, MembershipService service, CancellationToken ct) =>
            {
                await service.LeaveAsync(workspaceId, ct);
                return Results.NoContent();
            })
            .WithName("LeaveWorkspace");

        group.MapPost("/{workspaceId:guid}/transfer-ownership", async (
                Guid workspaceId, TransferOwnershipRequest request,
                MembershipService service, CancellationToken ct) =>
            {
                await service.TransferOwnershipAsync(new TransferOwnershipCommand(workspaceId, request.MembershipId), ct);
                return Results.NoContent();
            })
            .AddEndpointFilter<ValidationFilter<TransferOwnershipRequest>>()
            .WithName("TransferWorkspaceOwnership");

        // POST, not DELETE: irreversible and needs a confirmation body, matching /leave and
        // /transfer-ownership above. Owner-only; the service also requires the retyped slug to match.
        group.MapPost("/{workspaceId:guid}/delete", async (
                Guid workspaceId, DeleteWorkspaceRequest request,
                WorkspaceDeletionService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(workspaceId, request.ConfirmSlug, ct);
                return Results.NoContent();
            })
            .AddEndpointFilter<ValidationFilter<DeleteWorkspaceRequest>>()
            .WithName("DeleteWorkspace");
    }

    private static void MapInvitations(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/invitations").RequireAuthorization();

        group.MapPost("/{token}/accept", async (
                string token, ICurrentUser user, InvitationService service, CancellationToken ct) =>
                Results.Ok(await service.AcceptAsync(token, user.UserId, user.Email, ct)))
            .WithName("AcceptInvitation");
    }

    private static void MapTeams(RouteGroupBuilder api)
    {
        var workspaceTeams = api.MapGroup("/workspaces/{workspaceId:guid}/teams").RequireAuthorization();

        workspaceTeams.MapPost("/", async (Guid workspaceId, CreateTeamRequest request, TeamService service, CancellationToken ct) =>
            {
                var dto = await service.CreateAsync(new CreateTeamCommand(workspaceId, request.Name, request.Description), ct);
                return Results.Created($"/api/v1/teams/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateTeamRequest>>()
            .WithName("CreateTeam");

        workspaceTeams.MapGet("/", async (Guid workspaceId, TeamService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(workspaceId, ct)))
            .WithName("ListTeams");

        var teams = api.MapGroup("/teams").RequireAuthorization();

        teams.MapPatch("/{teamId:guid}", async (Guid teamId, UpdateTeamRequest request, TeamService service, CancellationToken ct) =>
                Results.Ok(await service.UpdateAsync(teamId, new UpdateTeamCommand(request.Name, request.Description), ct)))
            .AddEndpointFilter<ValidationFilter<UpdateTeamRequest>>()
            .WithName("UpdateTeam");

        teams.MapPost("/{teamId:guid}/archive", async (Guid teamId, TeamService service, CancellationToken ct) =>
        {
            await service.SetArchivedAsync(teamId, true, ct);
            return Results.NoContent();
        });

        teams.MapPost("/{teamId:guid}/restore", async (Guid teamId, TeamService service, CancellationToken ct) =>
        {
            await service.SetArchivedAsync(teamId, false, ct);
            return Results.NoContent();
        });

        teams.MapDelete("/{teamId:guid}", async (Guid teamId, TeamService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(teamId, ct);
            return Results.NoContent();
        });

        teams.MapGet("/{teamId:guid}/members", async (Guid teamId, TeamService service, CancellationToken ct) =>
                Results.Ok(await service.ListMembersAsync(teamId, ct)))
            .WithName("ListTeamMembers");

        teams.MapPost("/{teamId:guid}/members", async (Guid teamId, TeamMemberRequest request, TeamService service, CancellationToken ct) =>
        {
            await service.AddMemberAsync(teamId, request.UserId, ct);
            return Results.NoContent();
        });

        teams.MapDelete("/{teamId:guid}/members/{userId:guid}", async (Guid teamId, Guid userId, TeamService service, CancellationToken ct) =>
        {
            await service.RemoveMemberAsync(teamId, userId, ct);
            return Results.NoContent();
        });
    }

    private static void MapFeatures(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/features").RequireAuthorization();

        group.MapGet("/", async (Guid? workspaceId, FeatureService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(workspaceId, ct)))
            .WithName("ListFeatures");
    }

    // ADR-0003: read-only role/permission listing. Custom-role CRUD is future work; the data
    // model already supports a non-built-in role, this only exposes what already exists.
    private static void MapRoles(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/workspaces/{workspaceId:guid}/roles").RequireAuthorization();

        group.MapGet("/", async (Guid workspaceId, RoleService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(workspaceId, ct)))
            .WithName("ListWorkspaceRoles");
    }
}
