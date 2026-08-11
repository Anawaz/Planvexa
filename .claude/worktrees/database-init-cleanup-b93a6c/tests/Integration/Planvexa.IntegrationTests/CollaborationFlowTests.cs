namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

internal sealed record CommentResp(
    Guid Id, Guid TaskId, Guid? ParentId, Guid AuthorUserId, string Body, bool IsEdited, bool IsDeleted,
    List<Guid> MentionUserIds, List<ReactionResp> Reactions, List<CommentResp> Replies);
internal sealed record ReactionResp(string Emoji, List<Guid> UserIds);
internal sealed record NotificationResp(Guid Id, string EventType, string EntityType, Guid EntityId, DateTimeOffset? ReadAtUtc);
internal sealed record PreferenceResp(string EventType, bool Inbox, bool Email, bool Push);
internal sealed record ShareResp(Guid Id, Guid TaskId, string Token, string Url, DateTimeOffset? ExpiresAtUtc, bool RequiresPassword, string PermissionLevel);
internal sealed record SharedTaskResp(Guid TaskId, string Title, string? Description, bool IsCompleted, bool AllowsComments);

[Collection("api")]
public sealed class CollaborationFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Comment_and_threaded_reply_and_reaction()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Discuss");

        var top = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "First!" });
        top.StatusCode.ShouldBe(HttpStatusCode.Created);
        var topComment = await top.Content.ReadFromJsonAsync<CommentResp>();

        var reply = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "A reply", parentId = topComment!.Id });
        reply.StatusCode.ShouldBe(HttpStatusCode.Created);

        // A reply-to-a-reply is rejected (one level of threading).
        var replyResp = await reply.Content.ReadFromJsonAsync<CommentResp>();
        var deepReply = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "too deep", parentId = replyResp!.Id });
        deepReply.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Reaction toggles.
        (await client.PostAsJsonAsync($"/api/v1/comments/{topComment.Id}/reactions", new { emoji = "🎉" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var threads = await client.GetFromJsonAsync<List<CommentResp>>($"/api/v1/tasks/{task.Id}/comments");
        var loadedTop = threads!.Single(c => c.Id == topComment.Id);
        loadedTop.Replies.Count.ShouldBe(1);
        loadedTop.Reactions.ShouldContain(r => r.Emoji == "🎉");
    }

    /// <summary>
    /// Offline-mutation-outbox replay guard: a comment post replayed with the same Idempotency-Key header
    /// must return the ORIGINAL comment, not insert a second row — see CommentService.AddAsync's
    /// idempotency check.
    /// </summary>
    [Fact]
    public async Task Repeated_comment_post_with_the_same_idempotency_key_does_not_duplicate()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Discuss");
        var idempotencyKey = Guid.NewGuid().ToString();

        async Task<CommentResp> PostWithKeyAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tasks/{task.Id}/comments")
            {
                Content = JsonContent.Create(new { body = "Offline-created comment" }),
            };
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            var response = await client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<CommentResp>())!;
        }

        var first = await PostWithKeyAsync();
        var replay = await PostWithKeyAsync();

        replay.Id.ShouldBe(first.Id);

        var threads = await client.GetFromJsonAsync<List<CommentResp>>($"/api/v1/tasks/{task.Id}/comments");
        threads!.Count(c => c.Body == "Offline-created comment").ShouldBe(1);
    }

    [Fact]
    public async Task Mention_creates_inbox_notification_and_email_delivery()
    {
        // Owner + an invited member (the mention target).
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Needs review");

        // Owner mentions the member.
        var comment = await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments",
            new { body = "please look @member", mentionUserIds = new[] { memberUserId } });
        comment.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The member has an inbox notification.
        var unread = await memberClient.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count");
        unread.GetProperty("count").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        var notifications = await memberClient.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications?unreadOnly=true");
        notifications!.ShouldContain(n => n.EventType == "mention" && n.EntityId == task.Id);

        // The email delivery is drained by the background service within a few seconds.
        var mentionNotification = notifications!.First(n => n.EventType == "mention");
        await WaitForEmailAsync(memberUserId);

        // Mark read reduces unread count.
        (await memberClient.PostAsync(new Uri($"/api/v1/notifications/{mentionNotification.Id}/read", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterRead = await memberClient.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count");
        afterRead.GetProperty("count").GetInt32().ShouldBe(unread.GetProperty("count").GetInt32() - 1);
    }

    [Fact]
    public async Task Preference_disabling_email_suppresses_email_delivery()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "noemail");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        // Member disables email for mentions.
        (await memberClient.PutAsJsonAsync("/api/v1/notification-preferences/mention", new { inbox = true, email = false }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var emailsBefore = await fixture.EmailCountForAsync(memberUserId);

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "No email please");
        await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments",
            new { body = "@noemail", mentionUserIds = new[] { memberUserId } });

        // Inbox notification still arrives.
        var notifications = await memberClient.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications?unreadOnly=true");
        notifications!.ShouldContain(n => n.EventType == "mention");

        // Give the delivery loop time; no new email should be sent for this recipient.
        await Task.Delay(TimeSpan.FromSeconds(8));
        (await fixture.EmailCountForAsync(memberUserId)).ShouldBe(emailsBefore);
    }

    [Fact]
    public async Task Duplicate_notification_is_deduplicated()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "dedup");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Dedup");

        // Same comment mentions the member twice (deduped within the comment) => a single notification.
        await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments",
            new { body = "@dedup @dedup", mentionUserIds = new[] { memberUserId, memberUserId } });

        var notifications = await memberClient.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications");
        notifications!.Count(n => n.EntityId == task.Id).ShouldBe(1);
    }

    private async Task WaitForEmailAsync(Guid recipientUserId)
    {
        for (var i = 0; i < 20; i++)
        {
            if (await fixture.EmailCountForAsync(recipientUserId) > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException("No email was delivered within the timeout.");
    }
}
