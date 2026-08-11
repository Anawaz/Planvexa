namespace Planvexa.UnitTests.Chat;

using Planvexa.Modules.Chat.Domain;
using Planvexa.Modules.Chat.Authorization;
using Planvexa.SharedContracts.Workspaces;
using Shouldly;
using Xunit;

public sealed class ChatChannelTests
{
    private static ChatChannel New(bool isPrivate, Guid creator)
        => ChatChannel.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "general", "desc", isPrivate, creator, Guid.CreateVersion7, DateTimeOffset.UtcNow);

    [Fact]
    public void Create_adds_the_creator_as_a_member()
    {
        var creator = Guid.CreateVersion7();
        var channel = New(isPrivate: true, creator);
        channel.IsMember(creator).ShouldBeTrue();
        channel.Members.Count.ShouldBe(1);
    }

    [Fact]
    public void Public_channel_is_accessible_to_any_workspace_member()
    {
        var channel = New(isPrivate: false, Guid.CreateVersion7());
        var stranger = Guid.CreateVersion7();
        channel.CanBeAccessedBy(stranger, isWorkspaceMember: true).ShouldBeTrue();
        channel.CanBeAccessedBy(stranger, isWorkspaceMember: false).ShouldBeFalse();
    }

    [Fact]
    public void Private_channel_is_only_accessible_to_members()
    {
        var creator = Guid.CreateVersion7();
        var channel = New(isPrivate: true, creator);
        var stranger = Guid.CreateVersion7();

        channel.CanBeAccessedBy(stranger, isWorkspaceMember: true).ShouldBeFalse();
        channel.AddMember(Guid.CreateVersion7(), stranger, DateTimeOffset.UtcNow).ShouldBeTrue();
        channel.CanBeAccessedBy(stranger, isWorkspaceMember: true).ShouldBeTrue();
    }

    [Fact]
    public void AddMember_is_idempotent()
    {
        var channel = New(isPrivate: true, Guid.CreateVersion7());
        var user = Guid.CreateVersion7();
        channel.AddMember(Guid.CreateVersion7(), user, DateTimeOffset.UtcNow).ShouldBeTrue();
        channel.AddMember(Guid.CreateVersion7(), user, DateTimeOffset.UtcNow).ShouldBeFalse();
    }

    [Fact]
    public void Creator_cannot_be_removed()
    {
        var creator = Guid.CreateVersion7();
        var channel = New(isPrivate: true, creator);
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() => channel.RemoveMember(creator));
    }

    [Fact]
    public void Archive_is_idempotent()
    {
        var channel = New(isPrivate: false, Guid.CreateVersion7());
        channel.Archive(DateTimeOffset.Parse("2026-03-01Z"));
        var first = channel.ArchivedAtUtc;
        channel.Archive(DateTimeOffset.Parse("2026-04-01Z"));
        channel.ArchivedAtUtc.ShouldBe(first);
        channel.IsArchived.ShouldBeTrue();
    }

    [Fact]
    public void CreateLinked_produces_a_non_private_channel_with_the_linked_resource_recorded()
    {
        var creator = Guid.CreateVersion7();
        var listId = Guid.CreateVersion7();
        var channel = ChatChannel.CreateLinked(
            Guid.CreateVersion7(), Guid.CreateVersion7(), ChatChannelType.List, "Backlog", null,
            ChatLinkedResourceTypes.List, listId, creator, Guid.CreateVersion7, DateTimeOffset.UtcNow);

        channel.ChannelType.ShouldBe(ChatChannelType.List);
        channel.LinkedResourceType.ShouldBe(ChatLinkedResourceTypes.List);
        channel.LinkedResourceId.ShouldBe(listId);
        channel.IsPrivate.ShouldBeFalse();

        // Structural (membership) access alone says "yes" for a non-member workspace member — the actual
        // gate for linked channels is the linked resource's ACL, applied by ChatChannelService, not here.
        channel.CanBeAccessedBy(Guid.CreateVersion7(), isWorkspaceMember: true).ShouldBeTrue();
    }

    [Fact]
    public void CreateLinked_rejects_a_non_linkable_channel_type()
    {
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            ChatChannel.CreateLinked(
                Guid.CreateVersion7(), Guid.CreateVersion7(), ChatChannelType.Workspace, "x", null,
                ChatLinkedResourceTypes.List, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CreateDirect_dm_requires_exactly_two_participants_and_is_membership_gated()
    {
        var creator = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var stranger = Guid.CreateVersion7();

        var dm = ChatChannel.CreateDirect(
            Guid.CreateVersion7(), Guid.CreateVersion7(), ChatChannelType.Dm, [creator, other],
            creator, Guid.CreateVersion7, DateTimeOffset.UtcNow);

        dm.IsPrivate.ShouldBeTrue();
        dm.Members.Count.ShouldBe(2);
        dm.IsMember(other).ShouldBeTrue();

        // No workspace-role-floor fallback: even a full workspace member who isn't a participant is denied.
        dm.CanBeAccessedBy(stranger, isWorkspaceMember: true).ShouldBeFalse();
        dm.CanBeAccessedBy(other, isWorkspaceMember: false).ShouldBeTrue();

        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            ChatChannel.CreateDirect(
                Guid.CreateVersion7(), Guid.CreateVersion7(), ChatChannelType.Dm, [creator, other, stranger],
                creator, Guid.CreateVersion7, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CreateDirect_group_dm_requires_at_least_three_participants()
    {
        var creator = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();

        var groupDm = ChatChannel.CreateDirect(
            Guid.CreateVersion7(), Guid.CreateVersion7(), ChatChannelType.GroupDm, [creator, b, c],
            creator, Guid.CreateVersion7, DateTimeOffset.UtcNow);
        groupDm.Members.Count.ShouldBe(3);

        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            ChatChannel.CreateDirect(
                Guid.CreateVersion7(), Guid.CreateVersion7(), ChatChannelType.GroupDm, [creator, b],
                creator, Guid.CreateVersion7, DateTimeOffset.UtcNow));
    }
}

public sealed class ChatMessageTests
{
    private static ChatMessage New(Guid author)
        => ChatMessage.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, author, "hello", DateTimeOffset.UtcNow);

    [Fact]
    public void Create_rejects_blank_body()
    {
        Should.Throw<System.ArgumentException>(() =>
            ChatMessage.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, Guid.CreateVersion7(), "   ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Only_the_author_can_edit()
    {
        var author = Guid.CreateVersion7();
        var message = New(author);
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ForbiddenException>(() => message.Edit("changed", Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        message.Edit("changed", author, DateTimeOffset.UtcNow);
        message.Body.ShouldBe("changed");
        message.EditedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void Author_can_delete_own_message()
    {
        var author = Guid.CreateVersion7();
        var message = New(author);
        message.Delete(author, isModerator: false, DateTimeOffset.UtcNow);
        message.IsDeleted.ShouldBeTrue();
        message.Body.ShouldBe(string.Empty);
    }

    [Fact]
    public void Non_author_needs_moderator_to_delete()
    {
        var message = New(Guid.CreateVersion7());
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ForbiddenException>(() => message.Delete(Guid.CreateVersion7(), isModerator: false, DateTimeOffset.UtcNow));

        message.Delete(Guid.CreateVersion7(), isModerator: true, DateTimeOffset.UtcNow);
        message.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public void Deleted_message_cannot_be_edited()
    {
        var author = Guid.CreateVersion7();
        var message = New(author);
        message.Delete(author, isModerator: false, DateTimeOffset.UtcNow);
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ConflictException>(() => message.Edit("x", author, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_records_valid_mentions_once_each()
    {
        var author = Guid.CreateVersion7();
        var mentioned = Guid.CreateVersion7();
        var message = ChatMessage.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, author, "hi @you", DateTimeOffset.UtcNow,
            [mentioned, mentioned], Guid.CreateVersion7);

        message.Mentions.Count.ShouldBe(1);
        message.Mentions[0].MentionedUserId.ShouldBe(mentioned);
    }

    [Fact]
    public void Reactions_are_unique_per_user_and_emoji()
    {
        var message = New(Guid.CreateVersion7());
        var user = Guid.CreateVersion7();

        message.AddReaction(Guid.CreateVersion7(), user, "👍").ShouldBeTrue();
        message.AddReaction(Guid.CreateVersion7(), user, "👍").ShouldBeFalse();
        message.Reactions.Count.ShouldBe(1);

        message.RemoveReaction(user, "👍").ShouldBeTrue();
        message.Reactions.ShouldBeEmpty();
    }

    [Fact]
    public void Cannot_react_to_a_deleted_message()
    {
        var author = Guid.CreateVersion7();
        var message = New(author);
        message.Delete(author, isModerator: false, DateTimeOffset.UtcNow);
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ConflictException>(() => message.AddReaction(Guid.CreateVersion7(), author, "👍"));
    }
}

public sealed class ChatAuthorizerTests
{
    [Theory]
    [InlineData(WorkspaceRole.Guest, false)]
    [InlineData(WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    public void Participate_requires_member(WorkspaceRole role, bool allowed)
        => ChatAuthorizer.CanParticipate(role).ShouldBe(allowed);

    [Theory]
    [InlineData(WorkspaceRole.Member, false)]
    [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Owner, true)]
    public void Moderator_requires_admin(WorkspaceRole role, bool allowed)
        => ChatAuthorizer.IsModerator(role).ShouldBe(allowed);
}
