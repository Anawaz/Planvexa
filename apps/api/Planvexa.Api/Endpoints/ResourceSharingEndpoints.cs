namespace Planvexa.Api.Endpoints;

using FluentValidation;
using Planvexa.Modules.WorkManagement.Application.Services;
using Planvexa.SharedContracts.Workspaces;

// ---- Request models ----
public sealed record GrantPermissionRequest(string PrincipalType, Guid PrincipalId, string Level);
public sealed record SetPrivateRequest(bool IsPrivate);

public sealed class GrantPermissionRequestValidator : AbstractValidator<GrantPermissionRequest>
{
    public GrantPermissionRequestValidator()
    {
        RuleFor(x => x.PrincipalType).Must(t => Enum.TryParse<Modules.Tenancy.Domain.ResourcePrincipalType>(t, true, out _))
            .WithMessage("PrincipalType must be one of: user, team, role.");
        RuleFor(x => x.PrincipalId).NotEmpty();
        RuleFor(x => x.Level).Must(l => Enum.TryParse<PermissionLevel>(l, true, out _))
            .WithMessage("Level must be one of: view, comment, edit, fullEdit, share, manage.");
    }
}

/// <summary>
/// ADR-0003: generic ACL grant/revoke + privacy-toggle endpoints, shared by every
/// WorkManagement resource type (space/folder/list/task) — resourceType is a route segment
/// ("space"/"folder"/"list"/"task") matched against ResourceSharingService's dispatch.
/// </summary>
public static class ResourceSharingEndpoints
{
    public static void MapResourceSharingEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/resources/{resourceType}/{resourceId:guid}").RequireAuthorization();

        group.MapGet("/permissions", async (string resourceType, Guid resourceId, ResourceSharingService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(resourceType, resourceId, ct)));

        group.MapPost("/permissions", async (
                string resourceType, Guid resourceId, GrantPermissionRequest r, ResourceSharingService svc, CancellationToken ct) =>
            {
                var level = Enum.Parse<PermissionLevel>(r.Level, ignoreCase: true);
                var grant = await svc.GrantAsync(resourceType, resourceId, new GrantResourcePermissionCommand(r.PrincipalType, r.PrincipalId, level), ct);
                return Results.Created($"/api/v1/resources/{resourceType}/{resourceId}/permissions", grant);
            })
            .AddEndpointFilter<ValidationFilter<GrantPermissionRequest>>();

        group.MapDelete("/permissions/{principalType}/{principalId:guid}", async (
            string resourceType, Guid resourceId, string principalType, Guid principalId, ResourceSharingService svc, CancellationToken ct) =>
        {
            await svc.RevokeAsync(resourceType, resourceId, principalType, principalId, ct);
            return Results.NoContent();
        });

        group.MapPatch("/private", async (
            string resourceType, Guid resourceId, SetPrivateRequest r, ResourceSharingService svc, CancellationToken ct) =>
            Results.Ok(new { isPrivate = await svc.SetPrivateAsync(resourceType, resourceId, r.IsPrivate, ct) }));
    }
}
