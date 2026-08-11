namespace Planvexa.UnitTests.Collaboration;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Collaboration.Domain;
using Planvexa.Modules.Notifications.Application;
using Planvexa.Modules.Notifications.Domain;
using Planvexa.SharedContracts.Notifications;
using Planvexa.SharedContracts.Workspaces;
using Shouldly;
using Xunit;

public sealed class CommentDomainTests
{
    private static Comment NewComment(Guid author, IReadOnlyCollection<Guid>? mentions = null)
        => Comment.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            parentId: null, author, "Hello @team", mentions ?? Array.Empty<Guid>(), Guid.CreateVersion7, DateTimeOffset.UtcNow);

    [Fact]
    public void Create_raises_comment_posted_and_mention_events()
    {
        var author = Guid.CreateVersion7();
        var mentioned = Guid.CreateVersion7();
        var comment = NewComment(author, new[] { mentioned });

        comment.Mentions.ShouldHaveSingleItem();
        // One CommentPosted + one UserMentioned event.
        comment.DomainEvents.Count.ShouldBe(2);
    }

    [Fact]
    public void Create_stores_the_idempotency_key_when_supplied()
    {
        var comment = Comment.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            parentId: null, Guid.CreateVersion7(), "Hello", Array.Empty<Guid>(), Guid.CreateVersion7,
            DateTimeOffset.UtcNow, idempotencyKey: "outbox-key-1");

        comment.IdempotencyKey.ShouldBe("outbox-key-1");
    }

    [Fact]
    public void Self_mention_does_not_raise_a_mention_event()
    {
        var author = Guid.CreateVersion7();
        var comment = NewComment(author, new[] { author });

        comment.Mentions.ShouldHaveSingleItem();
        comment.DomainEvents.Count.ShouldBe(1); // only CommentPosted
    }

    [Fact]
    public void Only_author_can_edit()
    {
        var author = Guid.CreateVersion7();
        var comment = NewComment(author);

        Should.Throw<ForbiddenException>(() => comment.Edit("changed", Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        Should.NotThrow(() => comment.Edit("changed", author, DateTimeOffset.UtcNow));
        comment.IsEdited.ShouldBeTrue();
    }

    [Fact]
    public void Reactions_are_unique_per_user_and_emoji()
    {
        var comment = NewComment(Guid.CreateVersion7());
        var user = Guid.CreateVersion7();

        comment.AddReaction(Guid.CreateVersion7(), user, "👍").ShouldBeTrue();
        comment.AddReaction(Guid.CreateVersion7(), user, "👍").ShouldBeFalse();
        comment.RemoveReaction(user, "👍").ShouldBeTrue();
        comment.Reactions.ShouldBeEmpty();
    }

    [Fact]
    public void SoftDelete_clears_body_and_is_idempotent()
    {
        var comment = NewComment(Guid.CreateVersion7());
        var actor = Guid.CreateVersion7();

        comment.SoftDelete(actor, DateTimeOffset.UtcNow);
        comment.IsDeleted.ShouldBeTrue();
        comment.Body.ShouldBeEmpty();
        Should.NotThrow(() => comment.SoftDelete(actor, DateTimeOffset.UtcNow));
    }
}

public sealed class ShareLinkDomainTests
{
    [Fact]
    public void Token_is_hashed_not_stored_raw()
    {
        var (link, raw) = PublicShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, null);

        raw.ShouldNotBeNullOrWhiteSpace();
        link.TokenHash.ShouldBe(PublicShareLink.HashToken(raw));
        link.TokenHash.ShouldNotBe(raw);
    }

    [Fact]
    public void Usable_reflects_revoke_and_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var (link, _) = PublicShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), now, TimeSpan.FromDays(1));

        link.IsUsable(now).ShouldBeTrue();
        link.IsUsable(now.AddDays(2)).ShouldBeFalse(); // expired
        link.Revoke();
        link.IsUsable(now).ShouldBeFalse(); // revoked
    }

    [Fact]
    public void Defaults_to_view_only_and_never_allows_comments()
    {
        var (link, _) = PublicShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, null);

        link.Level.ShouldBe(PermissionLevel.View);
        link.AllowsComments.ShouldBeFalse();
    }

    [Fact]
    public void Comment_level_grants_AllowsComments()
    {
        var (link, _) = PublicShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, PermissionLevel.Comment);

        link.AllowsComments.ShouldBeTrue();
    }

    [Theory]
    [InlineData(PermissionLevel.Edit)]
    [InlineData(PermissionLevel.FullEdit)]
    [InlineData(PermissionLevel.Share)]
    [InlineData(PermissionLevel.Manage)]
    public void Edit_and_above_are_never_grantable_via_a_public_link(PermissionLevel level)
    {
        Should.Throw<ValidationAppException>(() => PublicShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, level));

        var (link, _) = PublicShareLink.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, null);
        Should.Throw<ValidationAppException>(() => link.SetPermissionLevel(level));
    }
}

public sealed class NotificationPolicyTests
{
    [Fact]
    public void Default_is_inbox_plus_email_when_no_preference()
    {
        var channels = NotificationPolicy.Resolve("mention", null);
        channels.HasFlag(NotificationChannels.Inbox).ShouldBeTrue();
        channels.HasFlag(NotificationChannels.Email).ShouldBeTrue();
    }

    [Fact]
    public void Preference_can_disable_email()
    {
        var pref = NotificationPreference.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "mention", inbox: true, email: false);
        var channels = NotificationPolicy.Resolve("mention", pref);

        channels.HasFlag(NotificationChannels.Inbox).ShouldBeTrue();
        channels.HasFlag(NotificationChannels.Email).ShouldBeFalse();
    }

    [Fact]
    public void Preference_can_disable_all_channels()
    {
        var pref = NotificationPreference.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "mention", inbox: false, email: false);
        NotificationPolicy.Resolve("mention", pref).ShouldBe(NotificationChannels.None);
    }

    [Fact]
    public void Push_is_off_by_default_and_requires_explicit_opt_in()
    {
        NotificationPolicy.DefaultChannels("mention").HasFlag(NotificationChannels.Push).ShouldBeFalse();

        var pref = NotificationPreference.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "mention", inbox: true, email: true, push: true);
        NotificationPolicy.Resolve("mention", pref).HasFlag(NotificationChannels.Push).ShouldBeTrue();
    }
}

public sealed class NotificationDomainTests
{
    [Fact]
    public void Inbox_delivery_is_immediately_sent_email_is_pending()
    {
        var n = Notification.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "mention", "Task", Guid.CreateVersion7(), null, "dedup-1", DateTimeOffset.UtcNow);

        var inbox = n.AddDelivery(Guid.CreateVersion7(), NotificationChannels.Inbox, DateTimeOffset.UtcNow);
        var email = n.AddDelivery(Guid.CreateVersion7(), NotificationChannels.Email, DateTimeOffset.UtcNow);

        inbox.Status.ShouldBe(DeliveryStatus.Sent);
        email.Status.ShouldBe(DeliveryStatus.Pending);
    }

    [Fact]
    public void MarkRead_is_idempotent()
    {
        var n = Notification.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "mention", "Task", Guid.CreateVersion7(), null, "dedup-2", DateTimeOffset.UtcNow);

        var t1 = DateTimeOffset.UtcNow;
        n.MarkRead(t1);
        var firstReadAt = n.ReadAtUtc;
        n.MarkRead(t1.AddMinutes(5));
        n.ReadAtUtc.ShouldBe(firstReadAt);
    }

    [Fact]
    public void Delivery_fails_then_gives_up_after_retries()
    {
        var n = Notification.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "mention", "Task", Guid.CreateVersion7(), null, "dedup-3", DateTimeOffset.UtcNow);
        var delivery = n.AddDelivery(Guid.CreateVersion7(), NotificationChannels.Email, DateTimeOffset.UtcNow);

        for (var i = 0; i < 4; i++)
        {
            delivery.MarkFailed("boom");
            delivery.Status.ShouldBe(DeliveryStatus.Pending);
        }

        delivery.MarkFailed("boom"); // 5th attempt
        delivery.Status.ShouldBe(DeliveryStatus.Failed);
    }

    [Fact]
    public void Suppressed_delivery_does_not_consume_a_retry_attempt()
    {
        var n = Notification.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "mention", "Task", Guid.CreateVersion7(), null, "dedup-4", DateTimeOffset.UtcNow);
        var delivery = n.AddDelivery(Guid.CreateVersion7(), NotificationChannels.Push, DateTimeOffset.UtcNow);

        delivery.MarkSuppressed("No registered device for this user.");

        delivery.Status.ShouldBe(DeliveryStatus.Suppressed);
        delivery.Attempts.ShouldBe(0);
        delivery.Error.ShouldBe("No registered device for this user.");
    }
}

public sealed class DigestPreferenceTests
{
    [Fact]
    public void Off_is_never_due()
    {
        var pref = DigestPreference.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DigestFrequency.Off, DateTimeOffset.UtcNow);
        pref.IsDue(DateTimeOffset.UtcNow.AddYears(1)).ShouldBeFalse();
    }

    [Fact]
    public void Daily_is_due_after_24_hours_not_before()
    {
        var created = DateTimeOffset.UtcNow;
        var pref = DigestPreference.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DigestFrequency.Daily, created);

        pref.IsDue(created.AddHours(23)).ShouldBeFalse();
        pref.IsDue(created.AddHours(25)).ShouldBeTrue();
    }

    [Fact]
    public void MarkSent_resets_the_due_window()
    {
        var created = DateTimeOffset.UtcNow;
        var pref = DigestPreference.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DigestFrequency.Daily, created);

        pref.MarkSent(created.AddHours(25));
        pref.IsDue(created.AddHours(30)).ShouldBeFalse(); // only 5h since the last send
        pref.IsDue(created.AddHours(50)).ShouldBeTrue();
    }

    [Fact]
    public void Weekly_uses_a_seven_day_window()
    {
        var created = DateTimeOffset.UtcNow;
        var pref = DigestPreference.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DigestFrequency.Weekly, created);

        pref.IsDue(created.AddDays(6)).ShouldBeFalse();
        pref.IsDue(created.AddDays(8)).ShouldBeTrue();
    }
}
