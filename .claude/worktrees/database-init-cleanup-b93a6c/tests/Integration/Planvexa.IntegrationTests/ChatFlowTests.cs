namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

// Response shapes for Chat endpoints.
internal sealed record ChatChannelResp(Guid Id, string Name, string? Description, bool IsPrivate, bool IsArchived, Guid CreatedByUserId, DateTimeOffset CreatedAtUtc, List<Guid> MemberUserIds);
internal sealed record ChatChannelSummaryResp(Guid Id, string Name, string? Description, bool IsPrivate, bool IsArchived, DateTimeOffset CreatedAtUtc);
internal sealed record ChatMessageResp(Guid Id, Guid ChannelId, Guid? ParentMessageId, Guid AuthorUserId, string Body, bool IsDeleted, DateTimeOffset CreatedAtUtc, DateTimeOffset? EditedAtUtc);

[Collection("api")]
public sealed class ChatFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Public_channel_message_flow_post_edit_delete()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var create = await client.PostAsJsonAsync("/api/v1/chat/channels", new { name = "general", description = "Team chat", isPrivate = false });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelResp>())!;

        var post = await client.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "Hello team" });
        post.StatusCode.ShouldBe(HttpStatusCode.Created);
        var message = (await post.Content.ReadFromJsonAsync<ChatMessageResp>())!;
        message.Body.ShouldBe("Hello team");

        // Reply (threading).
        var reply = await client.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { parentMessageId = message.Id, body = "Welcome!" });
        reply.EnsureSuccessStatusCode();
        var replyMsg = (await reply.Content.ReadFromJsonAsync<ChatMessageResp>())!;
        replyMsg.ParentMessageId.ShouldBe(message.Id);

        // Edit own message.
        var edit = await client.PatchAsJsonAsync($"/api/v1/chat/messages/{message.Id}", new { body = "Hello everyone" });
        edit.EnsureSuccessStatusCode();
        (await edit.Content.ReadFromJsonAsync<ChatMessageResp>())!.Body.ShouldBe("Hello everyone");

        // List returns messages chronologically.
        var messages = await client.GetFromJsonAsync<List<ChatMessageResp>>($"/api/v1/chat/channels/{channel.Id}/messages");
        messages!.Count.ShouldBe(2);
        messages[0].Id.ShouldBe(message.Id);

        // Delete a message (soft delete clears body).
        var del = await client.DeleteAsync(new Uri($"/api/v1/chat/messages/{message.Id}", UriKind.Relative));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterDelete = await client.GetFromJsonAsync<List<ChatMessageResp>>($"/api/v1/chat/channels/{channel.Id}/messages");
        afterDelete!.Single(m => m.Id == message.Id).IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Private_channel_is_hidden_from_non_members_and_visible_to_added_members()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "chat-pm");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        // Owner creates a private channel WITHOUT the member.
        var create = await owner.PostAsJsonAsync("/api/v1/chat/channels", new { name = "secret", isPrivate = true });
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelResp>())!;

        // The member cannot see or read it.
        var memberList = await member.GetFromJsonAsync<List<ChatChannelSummaryResp>>("/api/v1/chat/channels");
        memberList!.ShouldNotContain(c => c.Id == channel.Id);
        (await member.GetAsync(new Uri($"/api/v1/chat/channels/{channel.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "sneaky" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Owner adds the member → now they can read + post.
        var add = await owner.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/members", new { userId = memberUserId });
        add.EnsureSuccessStatusCode();

        var memberListAfter = await member.GetFromJsonAsync<List<ChatChannelSummaryResp>>("/api/v1/chat/channels");
        memberListAfter!.ShouldContain(c => c.Id == channel.Id);
        (await member.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "thanks" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Member_cannot_edit_another_users_message_but_admin_can_delete_it()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "chat-mod");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var create = await owner.PostAsJsonAsync("/api/v1/chat/channels", new { name = "general", isPrivate = false });
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelResp>())!;

        // Member posts a message.
        var post = await member.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "member message" });
        var message = (await post.Content.ReadFromJsonAsync<ChatMessageResp>())!;

        // The owner (admin/moderator) cannot EDIT it (author-only)...
        var edit = await owner.PatchAsJsonAsync($"/api/v1/chat/messages/{message.Id}", new { body = "tampered" });
        edit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // ...but CAN delete it (moderator).
        var del = await owner.DeleteAsync(new Uri($"/api/v1/chat/messages/{message.Id}", UriKind.Relative));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Guest_cannot_post_messages()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var create = await owner.PostAsJsonAsync("/api/v1/chat/channels", new { name = "general", isPrivate = false });
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelResp>())!;

        var (guestSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "chat-g", role: "Guest");
        var guest = fixture.WorkClient(guestSubject, slug, workspaceId);

        var post = await guest.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "hi" });
        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Archived_channel_rejects_new_messages()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/chat/channels", new { name = "old", isPrivate = false });
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelResp>())!;

        (await client.PostAsync(new Uri($"/api/v1/chat/channels/{channel.Id}/archive", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var post = await client.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "too late" });
        post.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Channels_and_messages_are_isolated_between_tenants()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var createA = await clientA.PostAsJsonAsync("/api/v1/chat/channels", new { name = "a-only", isPrivate = false });
        var channelA = (await createA.Content.ReadFromJsonAsync<ChatChannelResp>())!;
        await clientA.PostAsJsonAsync($"/api/v1/chat/channels/{channelA.Id}/messages", new { body = "secret-a" });

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var listB = await clientB.GetFromJsonAsync<List<ChatChannelSummaryResp>>("/api/v1/chat/channels");
        listB!.ShouldNotContain(c => c.Name == "a-only");

        // Workspace B cannot read workspace A's channel or messages.
        (await clientB.GetAsync(new Uri($"/api/v1/chat/channels/{channelA.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await clientB.GetAsync(new Uri($"/api/v1/chat/channels/{channelA.Id}/messages", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Chat_messages_enforce_row_level_security_via_non_superuser_role()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var createA = await clientA.PostAsJsonAsync("/api/v1/chat/channels", new { name = "rls-a", isPrivate = false });
        var channelA = (await createA.Content.ReadFromJsonAsync<ChatChannelResp>())!;
        await clientA.PostAsJsonAsync($"/api/v1/chat/channels/{channelA.Id}/messages", new { body = "RLS-A-message" });

        var (clientB, workspaceB, _, _) = await fixture.NewWorkspaceClientAsync();
        var createB = await clientB.PostAsJsonAsync("/api/v1/chat/channels", new { name = "rls-b", isPrivate = false });
        var channelB = (await createB.Content.ReadFromJsonAsync<ChatChannelResp>())!;
        await clientB.PostAsJsonAsync($"/api/v1/chat/channels/{channelB.Id}/messages", new { body = "RLS-B-message" });

        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await using (var set = connection.CreateCommand())
        {
            set.CommandText = "SELECT set_config('app.current_workspace', @w, false)";
            set.Parameters.AddWithValue("w", workspaceB.ToString());
            await set.ExecuteNonQueryAsync();
        }

        var bodies = new List<string>();
        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT body FROM chat.messages";
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            bodies.Add(reader.GetString(0));
        }

        bodies.ShouldContain("RLS-B-message");
        bodies.ShouldNotContain("RLS-A-message");
    }
}
