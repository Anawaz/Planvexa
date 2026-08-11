namespace Planvexa.Api.Endpoints;

using Planvexa.Modules.Chat.Application;
using Planvexa.Modules.Chat.Application.Services;

// ---- Request models ----
public sealed record CreateChannelRequest(string Name, string? Description, bool IsPrivate, IReadOnlyList<Guid>? MemberUserIds);
public sealed record CreateLinkedChannelRequest(string LinkedResourceType, Guid LinkedResourceId, string Name, string? Description);
public sealed record CreateDirectMessageRequest(IReadOnlyList<Guid> ParticipantUserIds);
public sealed record UpdateChannelRequest(string? Name, string? Description);
public sealed record ChannelMemberRequest(Guid UserId);
public sealed record PostMessageRequest(Guid? ParentMessageId, string Body, IReadOnlyList<Guid>? MentionUserIds);
public sealed record EditMessageRequest(string Body);
public sealed record ChatReactionRequest(string Emoji);
public sealed record MarkChannelReadRequest(Guid? LastReadMessageId);

/// <summary>Chat channels + messages endpoints (workspace-scoped, realtime via SignalR).</summary>
public static class ChatEndpoints
{
    public static void MapChatEndpoints(this RouteGroupBuilder api)
    {
        MapChannels(api);
        MapMessages(api);
    }

    private static void MapChannels(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/chat/channels").RequireAuthorization();

        group.MapGet("/", async (ChatChannelService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapPost("/", async (CreateChannelRequest r, ChatChannelService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateAsync(new CreateChannelCommand(r.Name, r.Description, r.IsPrivate, r.MemberUserIds), ct);
            return Results.Created($"/api/v1/chat/channels/{dto.Id}", dto);
        });

        group.MapPost("/linked", async (CreateLinkedChannelRequest r, ChatChannelService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateLinkedAsync(new CreateLinkedChannelCommand(r.LinkedResourceType, r.LinkedResourceId, r.Name, r.Description), ct);
            return Results.Created($"/api/v1/chat/channels/{dto.Id}", dto);
        });

        group.MapPost("/direct", async (CreateDirectMessageRequest r, ChatChannelService svc, CancellationToken ct) =>
        {
            var dto = await svc.CreateDirectMessageAsync(new CreateDirectMessageCommand(r.ParticipantUserIds), ct);
            return Results.Created($"/api/v1/chat/channels/{dto.Id}", dto);
        });

        group.MapGet("/{id:guid}", async (Guid id, ChatChannelService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(id, ct)));

        group.MapPatch("/{id:guid}", async (Guid id, UpdateChannelRequest r, ChatChannelService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, new UpdateChannelCommand(r.Name, r.Description), ct)));

        group.MapPost("/{id:guid}/archive", async (Guid id, ChatChannelService svc, CancellationToken ct) =>
        {
            await svc.ArchiveAsync(id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/members", async (Guid id, ChannelMemberRequest r, ChatChannelService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddMemberAsync(id, r.UserId, ct)));

        group.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, ChatChannelService svc, CancellationToken ct) =>
            Results.Ok(await svc.RemoveMemberAsync(id, userId, ct)));

        group.MapPost("/{id:guid}/read", async (Guid id, MarkChannelReadRequest r, ChatChannelService svc, CancellationToken ct) =>
        {
            await svc.MarkReadAsync(id, new MarkChannelReadCommand(r.LastReadMessageId), ct);
            return Results.NoContent();
        });
    }

    private static void MapMessages(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/chat").RequireAuthorization();

        group.MapGet("/channels/{channelId:guid}/messages", async (Guid channelId, DateTimeOffset? before, ChatMessageService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(channelId, before, ct)));

        group.MapPost("/channels/{channelId:guid}/messages", async (Guid channelId, PostMessageRequest r, ChatMessageService svc, CancellationToken ct) =>
        {
            var dto = await svc.PostAsync(new PostMessageCommand(channelId, r.ParentMessageId, r.Body, r.MentionUserIds), ct);
            return Results.Created($"/api/v1/chat/messages/{dto.Id}", dto);
        });

        group.MapPatch("/messages/{messageId:guid}", async (Guid messageId, EditMessageRequest r, ChatMessageService svc, CancellationToken ct) =>
            Results.Ok(await svc.EditAsync(messageId, new EditMessageCommand(r.Body), ct)));

        group.MapDelete("/messages/{messageId:guid}", async (Guid messageId, ChatMessageService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(messageId, ct);
            return Results.NoContent();
        });

        group.MapPost("/messages/{messageId:guid}/reactions", async (Guid messageId, ChatReactionRequest r, ChatMessageService svc, CancellationToken ct) =>
            Results.Ok(await svc.AddReactionAsync(messageId, r.Emoji, ct)));

        group.MapDelete("/messages/{messageId:guid}/reactions/{emoji}", async (Guid messageId, string emoji, ChatMessageService svc, CancellationToken ct) =>
            Results.Ok(await svc.RemoveReactionAsync(messageId, Uri.UnescapeDataString(emoji), ct)));

        group.MapPost("/messages/{messageId:guid}/attachments", async (
                Guid messageId, IFormFile file, ChatAttachmentService svc, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                var dto = await svc.UploadAsync(messageId, file.FileName, file.ContentType, file.Length, content, ct);
                return Results.Created($"/api/v1/chat/attachments/{dto.Id}", dto);
            })
            // Minimal-API form binding demands an antiforgery token; this API is bearer-authenticated
            // and registers no CORS policy, so there is no cookie-driven cross-site request to forge
            // (same reasoning as WorkManagement's task-attachment upload endpoint).
            .DisableAntiforgery();

        group.MapGet("/attachments/{id:guid}/download", async (Guid id, ChatAttachmentService svc, CancellationToken ct) =>
        {
            var (attachment, content) = await svc.DownloadAsync(id, ct);
            return Results.Stream(content, attachment.ContentType, attachment.FileName);
        });

        group.MapDelete("/attachments/{id:guid}", async (Guid id, ChatAttachmentService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }
}
