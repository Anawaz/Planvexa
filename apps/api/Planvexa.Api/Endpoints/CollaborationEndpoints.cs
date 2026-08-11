namespace Planvexa.Api.Endpoints;

using FluentValidation;
using Planvexa.Modules.Collaboration.Application;
using Planvexa.Modules.Notifications.Application;

// ---- Request models ----
public sealed record CreateCommentRequest(string Body, Guid? ParentId, IReadOnlyList<Guid>? MentionUserIds);
public sealed record EditCommentRequest(string Body);
public sealed record ReactionRequest(string Emoji);
public sealed record SetPreferenceRequest(bool Inbox, bool Email, bool Push);
public sealed record SetDigestPreferenceRequest(string Frequency);
public sealed record CreateShareRequest(int? ExpiresInDays, string? Password, string? PermissionLevel);
public sealed record PostPublicCommentRequest(string? Password, string? GuestName, string Body);

public sealed class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(10000);
}

public sealed class EditCommentRequestValidator : AbstractValidator<EditCommentRequest>
{
    public EditCommentRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(10000);
}

public sealed class ReactionRequestValidator : AbstractValidator<ReactionRequest>
{
    public ReactionRequestValidator() => RuleFor(x => x.Emoji).NotEmpty().MaximumLength(32);
}

/// <summary>Collaboration, notification and sharing endpoints.</summary>
public static class CollaborationEndpoints
{
    public static void MapCollaborationEndpoints(this RouteGroupBuilder api)
    {
        MapComments(api);
        MapCommentAttachments(api);
        MapNotifications(api);
        MapSharing(api);
        MapPresence(api);
    }

    private static void MapComments(RouteGroupBuilder api)
    {
        api.MapPost("/tasks/{taskId:guid}/comments", async (Guid taskId, CreateCommentRequest r, HttpContext http, CommentService svc, CancellationToken ct) =>
            {
                var dto = await svc.AddAsync(new CreateCommentCommand(taskId, r.Body, r.ParentId, r.MentionUserIds), IdempotencyKey(http), ct);
                return Results.Created($"/api/v1/comments/{dto.Id}", dto);
            })
            .AddEndpointFilter<ValidationFilter<CreateCommentRequest>>()
            .RequireAuthorization();

        api.MapGet("/tasks/{taskId:guid}/comments", async (Guid taskId, CommentService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListForTaskAsync(taskId, ct))).RequireAuthorization();

        api.MapPatch("/comments/{id:guid}", async (Guid id, EditCommentRequest r, CommentService svc, CancellationToken ct) =>
                Results.Ok(await svc.EditAsync(id, r.Body, ct)))
            .AddEndpointFilter<ValidationFilter<EditCommentRequest>>()
            .RequireAuthorization();

        api.MapDelete("/comments/{id:guid}", async (Guid id, CommentService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        api.MapPost("/comments/{id:guid}/reactions", async (Guid id, ReactionRequest r, CommentService svc, CancellationToken ct) =>
                Results.Ok(await svc.AddReactionAsync(id, r.Emoji, ct)))
            .AddEndpointFilter<ValidationFilter<ReactionRequest>>()
            .RequireAuthorization();

        api.MapDelete("/comments/{id:guid}/reactions/{emoji}", async (Guid id, string emoji, CommentService svc, CancellationToken ct) =>
            Results.Ok(await svc.RemoveReactionAsync(id, emoji, ct))).RequireAuthorization();
    }

    /// <summary>Attachments on a Comment — same upload/list/download/delete shape as
    /// AttachmentEndpoints' Task attachments (ADR-0006: bearer-authenticated, Content-Disposition:
    /// attachment neutralises stored XSS, no MIME allowlist needed).</summary>
    private static void MapCommentAttachments(RouteGroupBuilder api)
    {
        api.MapPost("/comments/{id:guid}/attachments", async (
                Guid id, IFormFile file, CommentAttachmentService svc, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                var dto = await svc.UploadAsync(id, file.FileName, file.ContentType, file.Length, content, ct);
                return Results.Created($"/api/v1/comment-attachments/{dto.Id}", dto);
            })
            .RequireAuthorization()
            .DisableAntiforgery();

        api.MapGet("/comment-attachments/{id:guid}/download", async (Guid id, CommentAttachmentService svc, CancellationToken ct) =>
        {
            var (attachment, content) = await svc.DownloadAsync(id, ct);
            return Results.Stream(content, attachment.ContentType, attachment.FileName);
        }).RequireAuthorization();

        api.MapDelete("/comment-attachments/{id:guid}", async (Guid id, CommentAttachmentService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static void MapNotifications(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/notifications").RequireAuthorization();

        group.MapGet("/", async (bool? unreadOnly, int? limit, NotificationInboxService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(unreadOnly ?? false, Math.Clamp(limit ?? 50, 1, 200), ct)));

        group.MapGet("/unread-count", async (NotificationInboxService svc, CancellationToken ct) =>
            Results.Ok(new { count = await svc.UnreadCountAsync(ct) }));

        group.MapPost("/{id:guid}/read", async (Guid id, NotificationInboxService svc, CancellationToken ct) =>
        {
            await svc.MarkReadAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/read-all", async (NotificationInboxService svc, CancellationToken ct) =>
        {
            await svc.MarkAllReadAsync(ct);
            return Results.NoContent();
        });

        api.MapGet("/notification-preferences", async (NotificationInboxService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListPreferencesAsync(ct))).RequireAuthorization();

        api.MapPut("/notification-preferences/{eventType}", async (string eventType, SetPreferenceRequest r, NotificationInboxService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetPreferenceAsync(eventType, r.Inbox, r.Email, r.Push, ct))).RequireAuthorization();

        api.MapGet("/notification-preferences/digest", async (NotificationInboxService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDigestPreferenceAsync(ct))).RequireAuthorization();

        api.MapPut("/notification-preferences/digest", async (SetDigestPreferenceRequest r, NotificationInboxService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetDigestPreferenceAsync(r.Frequency, ct))).RequireAuthorization();
    }

    private static void MapSharing(RouteGroupBuilder api)
    {
        api.MapPost("/tasks/{taskId:guid}/share", async (Guid taskId, CreateShareRequest r, ShareLinkService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateAsync(taskId, r.ExpiresInDays, r.Password, ParsePermissionLevel(r.PermissionLevel), ct))).RequireAuthorization();

        api.MapGet("/tasks/{taskId:guid}/shares", async (Guid taskId, ShareLinkService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListForTaskAsync(taskId, ct))).RequireAuthorization();

        api.MapDelete("/shares/{id:guid}", async (Guid id, ShareLinkService svc, CancellationToken ct) =>
        {
            await svc.RevokeAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // For the link owner: guest comments left on a Comment-level link, and the full access log
        // (success + denied attempts, with IP) — see public-link-hardening note.
        api.MapGet("/shares/{id:guid}/comments", async (Guid id, ShareLinkService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicCommentsAsync(id, ct))).RequireAuthorization();

        api.MapGet("/shares/{id:guid}/access-log", async (Guid id, ShareLinkService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAccessLogAsync(id, ct))).RequireAuthorization();

        // Anonymous public read — exposes ONLY the shared task projection. ?password= verifies a
        // password-protected link; distinct 401 body shapes let the frontend prompt vs. show "wrong".
        api.MapGet("/public/tasks/{token}", async (string token, string? password, HttpContext http, ShareLinkService svc, CancellationToken ct) =>
        {
            var result = await svc.GetSharedTaskAsync(token, password, ClientIp(http), ct);
            return result.Status switch
            {
                ShareLinkAccessStatus.Ok => Results.Ok(result.Task),
                ShareLinkAccessStatus.PasswordRequired => Results.Json(new { requiresPassword = true }, statusCode: StatusCodes.Status401Unauthorized),
                ShareLinkAccessStatus.InvalidPassword => Results.Json(new { requiresPassword = true, invalid = true }, statusCode: StatusCodes.Status401Unauthorized),
                _ => Results.NotFound(),
            };
        }).AllowAnonymous();

        // Anonymous public comment — only accepted when the link's permission level is Comment, not
        // View. Never an edit path: there is no anonymous PATCH/DELETE for either the task or the comment.
        api.MapPost("/public/tasks/{token}/comments", async (string token, PostPublicCommentRequest r, HttpContext http, ShareLinkService svc, CancellationToken ct) =>
        {
            var result = await svc.AddPublicCommentAsync(token, r.Password, r.GuestName, r.Body, ClientIp(http), ct);
            return result.Status switch
            {
                PublicCommentPostStatus.Ok => Results.Created($"/api/v1/public/tasks/{token}/comments/{result.Comment!.Id}", result.Comment),
                PublicCommentPostStatus.PasswordRequired => Results.Json(new { requiresPassword = true }, statusCode: StatusCodes.Status401Unauthorized),
                PublicCommentPostStatus.InvalidPassword => Results.Json(new { requiresPassword = true, invalid = true }, statusCode: StatusCodes.Status401Unauthorized),
                PublicCommentPostStatus.Forbidden => Results.Json(new { error = "This link is view-only." }, statusCode: StatusCodes.Status403Forbidden),
                PublicCommentPostStatus.Invalid => Results.BadRequest(new { error = "Comment body is required." }),
                _ => Results.NotFound(),
            };
        }).AllowAnonymous();
    }

    private static Planvexa.SharedContracts.Workspaces.PermissionLevel? ParsePermissionLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse<Planvexa.SharedContracts.Workspaces.PermissionLevel>(value, ignoreCase: true, out var parsed))
        {
            throw new Planvexa.BuildingBlocks.Exceptions.ValidationAppException("Unsupported share permission level. Use View or Comment.");
        }

        return parsed;
    }

    /// <summary>Best-effort caller IP for access auditing — same source the global rate limiter already keys on.</summary>
    private static string? ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    /// <summary>Offline-mutation-outbox replay guard (mirrors AiMobileEndpoints.IdempotencyKey): empty/whitespace reads as absent.</summary>
    private static string? IdempotencyKey(HttpContext http)
    {
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    private static void MapPresence(RouteGroupBuilder api)
    {
        api.MapGet("/workspaces/{workspaceId:guid}/presence", async (
                Guid workspaceId,
                Planvexa.BuildingBlocks.Abstractions.ICurrentUser currentUser,
                Planvexa.SharedContracts.Workspaces.IWorkspaceAccessQuery access,
                Planvexa.Api.Realtime.PresenceTracker presence,
                CancellationToken ct) =>
            {
                if (await access.GetAccessAsync(workspaceId, currentUser.UserId, ct) is null)
                {
                    return Results.Ok(new { userIds = Array.Empty<Guid>() });
                }

                var group = Planvexa.Api.Realtime.RealtimeGroups.Workspace(workspaceId);
                return Results.Ok(new { userIds = presence.UsersIn(group) });
            }).RequireAuthorization();
    }
}
