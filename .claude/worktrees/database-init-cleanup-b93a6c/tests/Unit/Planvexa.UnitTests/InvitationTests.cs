namespace Planvexa.UnitTests.Tenancy;

using Planvexa.Modules.Tenancy.Domain;
using Shouldly;
using Xunit;

public sealed class InvitationTests
{
    private static readonly Guid Workspace = Guid.CreateVersion7();
    private static readonly Guid Inviter = Guid.CreateVersion7();

    [Fact]
    public void Create_returns_raw_token_whose_hash_is_stored()
    {
        var now = DateTimeOffset.UtcNow;
        var (invitation, rawToken) = Invitation.Create(
            Guid.CreateVersion7(), Workspace, "New.User@Example.com", MembershipRole.Member,
            Inviter, now, TimeSpan.FromDays(14));

        rawToken.ShouldNotBeNullOrWhiteSpace();
        invitation.TokenHash.ShouldBe(Invitation.HashToken(rawToken));
        invitation.TokenHash.ShouldNotBe(rawToken);
        invitation.Email.ShouldBe("new.user@example.com");
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    [Fact]
    public void Create_raises_member_invited_event()
    {
        var (invitation, _) = Invitation.Create(
            Guid.CreateVersion7(), Workspace, "a@b.com", MembershipRole.Member,
            Inviter, DateTimeOffset.UtcNow, TimeSpan.FromDays(14));

        invitation.DomainEvents.ShouldHaveSingleItem();
    }

    [Fact]
    public void IsExpired_is_true_after_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var (invitation, _) = Invitation.Create(
            Guid.CreateVersion7(), Workspace, "a@b.com", MembershipRole.Member,
            Inviter, now, TimeSpan.FromDays(1));

        invitation.IsExpired(now.AddHours(23)).ShouldBeFalse();
        invitation.IsExpired(now.AddDays(2)).ShouldBeTrue();
    }

    [Fact]
    public void Accept_marks_accepted_and_raises_event()
    {
        var (invitation, _) = Invitation.Create(
            Guid.CreateVersion7(), Workspace, "a@b.com", MembershipRole.Member,
            Inviter, DateTimeOffset.UtcNow, TimeSpan.FromDays(1));
        invitation.ClearDomainEvents();

        var userId = Guid.CreateVersion7();
        invitation.Accept(userId, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        invitation.Status.ShouldBe(InvitationStatus.Accepted);
        invitation.AcceptedByUserId.ShouldBe(userId);
        invitation.DomainEvents.ShouldHaveSingleItem();
    }

    [Fact]
    public void Rotate_replaces_token_reopens_and_reraises_event()
    {
        var now = DateTimeOffset.UtcNow;
        var (invitation, firstToken) = Invitation.Create(
            Guid.CreateVersion7(), Workspace, "a@b.com", MembershipRole.Member,
            Inviter, now, TimeSpan.FromDays(1));
        invitation.ClearDomainEvents();

        var rotatedToken = invitation.Rotate(now.AddDays(2), TimeSpan.FromDays(14));

        rotatedToken.ShouldNotBe(firstToken);
        invitation.TokenHash.ShouldBe(Invitation.HashToken(rotatedToken));
        invitation.TokenHash.ShouldNotBe(Invitation.HashToken(firstToken));
        invitation.Status.ShouldBe(InvitationStatus.Pending);
        invitation.ExpiresAtUtc.ShouldBe(now.AddDays(2).AddDays(14));
        invitation.DomainEvents.ShouldHaveSingleItem();
    }
}
