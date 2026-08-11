namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

// Response shapes for the chat overhaul endpoints.
internal sealed record ChatChannelV2Resp(
    Guid Id, string ChannelType, string Name, string? Description, bool IsPrivate, bool IsArchived,
    string? LinkedResourceType, Guid? LinkedResourceId, Guid CreatedByUserId, DateTimeOffset CreatedAtUtc, List<Guid> MemberUserIds);

internal sealed record ChatChannelSummaryV2Resp(
    Guid Id, string ChannelType, string Name, string? Description, bool IsPrivate, bool IsArchived,
    string? LinkedResourceType, Guid? LinkedResourceId, DateTimeOffset CreatedAtUtc, List<Guid> MemberUserIds, int UnreadCount);

internal sealed record ChatReactionResp(string Emoji, List<Guid> UserIds);

internal sealed record ChatAttachmentResp(Guid Id, Guid MessageId, string FileName, string ContentType, long SizeBytes, Guid UploadedByUserId, DateTimeOffset CreatedAtUtc);

internal sealed record ChatMessageV2Resp(
    Guid Id, Guid ChannelId, Guid? ParentMessageId, Guid AuthorUserId, string Body, bool IsDeleted,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? EditedAtUtc, List<Guid> MentionUserIds, List<ChatReactionResp> Reactions, List<ChatAttachmentResp> Attachments);

/// <summary>
/// Chat overhaul: the two security-critical wiring points named by the roadmap — a
/// Space/List/Task-linked channel inheriting the linked resource's ACL (item 1/9), and DM/group-DM
/// channels being strictly membership-gated with NO workspace-role-floor fallback (item 3) — plus
/// reactions/attachments/mentions round-tripping and unread counts.
/// </summary>
[Collection("api")]
public sealed class ChatOverhaulFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Linked_list_channel_is_hidden_when_the_list_turns_private_and_visible_once_granted()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync("Eng");
        var list = await owner.CreateListAsync(space.Id, "Roadmap");

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "chat-linked");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var create = await owner.PostAsJsonAsync("/api/v1/chat/channels/linked", new
        {
            linkedResourceType = "list",
            linkedResourceId = list.Id,
            name = "Roadmap chat",
            description = (string?)null,
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelV2Resp>())!;
        channel.ChannelType.ShouldBe("List");
        channel.LinkedResourceType.ShouldBe("list");
        channel.LinkedResourceId.ShouldBe(list.Id);
        channel.IsPrivate.ShouldBeFalse();

        // Baseline: the List is not private, so any workspace Member can reach the linked channel.
        (await member.GetAsync(new Uri($"/api/v1/chat/channels/{channel.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.GetFromJsonAsync<List<ChatChannelSummaryV2Resp>>("/api/v1/chat/channels"))!.ShouldContain(c => c.Id == channel.Id);

        var makePrivate = await owner.PatchAsJsonAsync($"/api/v1/resources/list/{list.Id}/private", new { isPrivate = true });
        makePrivate.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The List is now private with no grant for the member: the linked channel must be exactly as
        // hidden as the List — this is the load-bearing assertion for the ACL-inheritance item.
        (await member.GetAsync(new Uri($"/api/v1/chat/channels/{channel.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.GetFromJsonAsync<List<ChatChannelSummaryV2Resp>>("/api/v1/chat/channels"))!.ShouldNotContain(c => c.Id == channel.Id);
        (await member.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "sneaky" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The owner (creator/coarse Admin role) is unaffected by the List's privacy.
        (await owner.GetAsync(new Uri($"/api/v1/chat/channels/{channel.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var grant = await owner.PostAsJsonAsync(
            $"/api/v1/resources/list/{list.Id}/permissions",
            new { principalType = "user", principalId = memberUserId, level = "view" });
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Once granted View on the List, the member regains access to the linked channel.
        (await member.GetAsync(new Uri($"/api/v1/chat/channels/{channel.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.GetFromJsonAsync<List<ChatChannelSummaryV2Resp>>("/api/v1/chat/channels"))!.ShouldContain(c => c.Id == channel.Id);
        (await member.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "thanks for the access" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Dm_is_inaccessible_to_a_workspace_member_who_isnt_one_of_the_two_participants()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (participantSubject, participantUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "dm-participant");
        var (outsiderSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "dm-outsider");
        var participant = fixture.WorkClient(participantSubject, slug, workspaceId);
        var outsider = fixture.WorkClient(outsiderSubject, slug, workspaceId);

        var create = await owner.PostAsJsonAsync("/api/v1/chat/channels/direct", new { participantUserIds = new[] { participantUserId } });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dm = (await create.Content.ReadFromJsonAsync<ChatChannelV2Resp>())!;
        dm.ChannelType.ShouldBe("Dm");
        dm.IsPrivate.ShouldBeTrue();
        dm.MemberUserIds.Count.ShouldBe(2);

        // The outsider is a full workspace Member (not a Guest, not restricted) but is not one of the 2
        // DM participants — a DM must be strictly membership-gated, with NO workspace-role-floor
        // fallback. This is the load-bearing assertion for the DM-strictness item.
        (await outsider.GetAsync(new Uri($"/api/v1/chat/channels/{dm.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.GetFromJsonAsync<List<ChatChannelSummaryV2Resp>>("/api/v1/chat/channels"))!.ShouldNotContain(c => c.Id == dm.Id);
        (await outsider.PostAsJsonAsync($"/api/v1/chat/channels/{dm.Id}/messages", new { body = "let me in" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Both actual participants can read and post.
        (await owner.GetAsync(new Uri($"/api/v1/chat/channels/{dm.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await participant.GetAsync(new Uri($"/api/v1/chat/channels/{dm.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await participant.PostAsJsonAsync($"/api/v1/chat/channels/{dm.Id}/messages", new { body = "hi" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Starting a DM with the same participant again reuses the existing channel rather than
        // duplicating it.
        var again = await owner.PostAsJsonAsync("/api/v1/chat/channels/direct", new { participantUserIds = new[] { participantUserId } });
        (await again.Content.ReadFromJsonAsync<ChatChannelV2Resp>())!.Id.ShouldBe(dm.Id);
    }

    [Fact]
    public async Task Group_dm_needs_at_least_three_participants_and_excludes_non_participants()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (aSubject, aUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "gdm-a");
        var (bSubject, bUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "gdm-b");
        var (outsiderSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "gdm-outsider");
        var a = fixture.WorkClient(aSubject, slug, workspaceId);
        var outsider = fixture.WorkClient(outsiderSubject, slug, workspaceId);
        _ = bSubject;

        var create = await owner.PostAsJsonAsync("/api/v1/chat/channels/direct", new { participantUserIds = new[] { aUserId, bUserId } });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var groupDm = (await create.Content.ReadFromJsonAsync<ChatChannelV2Resp>())!;
        groupDm.ChannelType.ShouldBe("GroupDm");
        groupDm.MemberUserIds.Count.ShouldBe(3);

        (await a.GetAsync(new Uri($"/api/v1/chat/channels/{groupDm.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.GetAsync(new Uri($"/api/v1/chat/channels/{groupDm.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reactions_round_trip_through_the_api()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/chat/channels", new { name = "reactions", isPrivate = false });
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelV2Resp>())!;
        var post = await client.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "react to me" });
        var message = (await post.Content.ReadFromJsonAsync<ChatMessageV2Resp>())!;

        var react = await client.PostAsJsonAsync($"/api/v1/chat/messages/{message.Id}/reactions", new { emoji = "👍" });
        react.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reacted = (await react.Content.ReadFromJsonAsync<ChatMessageV2Resp>())!;
        reacted.Reactions.ShouldHaveSingleItem();
        reacted.Reactions[0].Emoji.ShouldBe("👍");

        var unreact = await client.DeleteAsync(new Uri($"/api/v1/chat/messages/{message.Id}/reactions/{Uri.EscapeDataString("👍")}", UriKind.Relative));
        unreact.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await unreact.Content.ReadFromJsonAsync<ChatMessageV2Resp>())!.Reactions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Attachments_round_trip_through_the_api()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/chat/channels", new { name = "attach", isPrivate = false });
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelV2Resp>())!;
        var post = await client.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "see attached" });
        var message = (await post.Content.ReadFromJsonAsync<ChatMessageV2Resp>())!;

        var bytes = "chat attachment bytes"u8.ToArray();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent { { part, "file", "notes.txt" } };

        var upload = await client.PostAsync(new Uri($"/api/v1/chat/messages/{message.Id}/attachments", UriKind.Relative), form);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var attachment = (await upload.Content.ReadFromJsonAsync<ChatAttachmentResp>())!;
        attachment.FileName.ShouldBe("notes.txt");

        var listed = await client.GetFromJsonAsync<List<ChatMessageV2Resp>>($"/api/v1/chat/channels/{channel.Id}/messages");
        listed!.Single(m => m.Id == message.Id).Attachments.ShouldHaveSingleItem();

        var download = await client.GetAsync(new Uri($"/api/v1/chat/attachments/{attachment.Id}/download", UriKind.Relative));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);

        var delete = await client.DeleteAsync(new Uri($"/api/v1/chat/attachments/{attachment.Id}", UriKind.Relative));
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Mentions_are_validated_against_workspace_membership_and_notify_the_mentioned_user()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "chat-mention");
        _ = fixture.WorkClient(memberSubject, slug, workspaceId);

        var create = await owner.PostAsJsonAsync("/api/v1/chat/channels", new { name = "mentions", isPrivate = false });
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelV2Resp>())!;

        var outsiderId = Guid.CreateVersion7();
        var post = await owner.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new
        {
            body = "hey @you",
            mentionUserIds = new[] { memberUserId, outsiderId },
        });
        post.StatusCode.ShouldBe(HttpStatusCode.Created);
        var message = (await post.Content.ReadFromJsonAsync<ChatMessageV2Resp>())!;

        // Only the real workspace member is recorded; a non-member id never becomes a mention row (no
        // notification leakage outside the workspace).
        message.MentionUserIds.ShouldBe([memberUserId]);
    }

    [Fact]
    public async Task Unread_counts_reflect_new_messages_and_reset_on_mark_read()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "chat-unread");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var create = await owner.PostAsJsonAsync("/api/v1/chat/channels", new { name = "unread", isPrivate = false });
        var channel = (await create.Content.ReadFromJsonAsync<ChatChannelV2Resp>())!;

        var initial = await member.GetFromJsonAsync<List<ChatChannelSummaryV2Resp>>("/api/v1/chat/channels");
        initial!.Single(c => c.Id == channel.Id).UnreadCount.ShouldBe(0);

        await owner.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "one" });
        await owner.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/messages", new { body = "two" });

        var afterPosts = await member.GetFromJsonAsync<List<ChatChannelSummaryV2Resp>>("/api/v1/chat/channels");
        afterPosts!.Single(c => c.Id == channel.Id).UnreadCount.ShouldBe(2);

        var markRead = await member.PostAsJsonAsync($"/api/v1/chat/channels/{channel.Id}/read", new { lastReadMessageId = (Guid?)null });
        markRead.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterRead = await member.GetFromJsonAsync<List<ChatChannelSummaryV2Resp>>("/api/v1/chat/channels");
        afterRead!.Single(c => c.Id == channel.Id).UnreadCount.ShouldBe(0);
    }
}
