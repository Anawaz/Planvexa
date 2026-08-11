namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>Workspace people administration — role changes, deactivate/reactivate,
/// leave, transfer ownership, last-owner protection, and pending-invitation management.</summary>
[Collection("api")]
public sealed class MembersManagementTests(PlanvexaFixture fixture)
{
    private sealed record PendingInvitationResponse(Guid Id, string Email, string Role, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

    private async Task<(HttpClient Owner, string OwnerSubject, WorkspaceResponse Workspace)> SetupAsync(string prefix)
    {
        var ownerSubject = TestData.NewSubject();
        var slug = TestData.NewSlug(prefix);
        var (response, org) = await fixture.AuthClient(ownerSubject).RegisterOrgAsync(slug);
        response.EnsureSuccessStatusCode();

        var owner = fixture.AuthClient(ownerSubject, org.Id);
        return (owner, ownerSubject, org);
    }

    private static async Task<Guid> MembershipIdOfAsync(HttpClient owner, Guid workspaceId, Guid userId)
    {
        var members = await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members");
        return members!.Single(m => m.UserId == userId).Id;
    }

    [Fact]
    public async Task Owner_changes_member_role()
    {
        var (owner, _, ws) = await SetupAsync("mrole");
        var (_, memberUserId) = await fixture.InviteMemberAsync(owner, ws.Id, "role");
        var membershipId = await MembershipIdOfAsync(owner, ws.Id, memberUserId);

        var response = await owner.PatchAsJsonAsync($"/api/v1/workspaces/{ws.Id}/members/{membershipId}", new { role = "Admin" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var members = await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{ws.Id}/members");
        members!.Single(m => m.Id == membershipId).Role.ShouldBe("Admin");
    }

    [Fact]
    public async Task Owner_deactivates_and_reactivates_member()
    {
        var (owner, _, ws) = await SetupAsync("mdeact");
        var (_, memberUserId) = await fixture.InviteMemberAsync(owner, ws.Id, "deact");
        var membershipId = await MembershipIdOfAsync(owner, ws.Id, memberUserId);

        (await owner.PostAsync(new Uri($"/api/v1/workspaces/{ws.Id}/members/{membershipId}/deactivate", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterDeactivate = await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{ws.Id}/members");
        afterDeactivate!.Single(m => m.Id == membershipId).Status.ShouldBe("Deactivated");

        (await owner.PostAsync(new Uri($"/api/v1/workspaces/{ws.Id}/members/{membershipId}/reactivate", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterReactivate = await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{ws.Id}/members");
        afterReactivate!.Single(m => m.Id == membershipId).Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Last_owner_cannot_leave_or_be_demoted()
    {
        var (owner, _, ws) = await SetupAsync("lastowner");

        // Resolve the owner's own membership id.
        var me = await owner.GetFromJsonAsync<CurrentUserResponse>("/api/v1/users/me");
        var ownerMembershipId = await MembershipIdOfAsync(owner, ws.Id, me!.UserId);

        // The sole Owner cannot leave.
        (await owner.PostAsync(new Uri($"/api/v1/workspaces/{ws.Id}/leave", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // The sole Owner cannot be demoted.
        (await owner.PatchAsJsonAsync($"/api/v1/workspaces/{ws.Id}/members/{ownerMembershipId}", new { role = "Admin" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Transfer_ownership_promotes_target_and_demotes_caller()
    {
        var (owner, _, ws) = await SetupAsync("xfer");
        var (_, memberUserId) = await fixture.InviteMemberAsync(owner, ws.Id, "heir");
        var heirMembershipId = await MembershipIdOfAsync(owner, ws.Id, memberUserId);

        var transfer = await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/transfer-ownership", new { membershipId = heirMembershipId });
        transfer.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var members = await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{ws.Id}/members");
        members!.Single(m => m.Id == heirMembershipId).Role.ShouldBe("Owner");
        members!.Count(m => m.Role == "Owner").ShouldBe(1);
        members!.ShouldContain(m => m.Role == "Admin");

        // After transfer the former owner (now Admin) can leave.
        (await owner.PostAsync(new Uri($"/api/v1/workspaces/{ws.Id}/leave", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Pending_invitations_are_listed_without_tokens_and_can_be_resent_and_revoked()
    {
        var (owner, _, ws) = await SetupAsync("pend");
        var email = $"pending-{Guid.NewGuid():N}@planvexa.test";
        var invite = await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/invitations", new { email, role = "Member" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The API response must never leak a raw token — it is delivered as an email link instead.
        (await invite.Content.ReadAsStringAsync()).ToLowerInvariant().ShouldNotContain("token");
        var createdToken = fixture.LastInvitationToken(email);
        createdToken.ShouldNotBeNullOrWhiteSpace();

        var pending = await owner.GetFromJsonAsync<List<PendingInvitationResponse>>($"/api/v1/workspaces/{ws.Id}/invitations");
        var row = pending!.Single(p => p.Email == email);
        row.Status.ShouldBe("Pending");

        // Resend rotates the token: the old raw token no longer works.
        var resend = await owner.PostAsync(new Uri($"/api/v1/workspaces/{ws.Id}/invitations/{row.Id}/resend", UriKind.Relative), null);
        resend.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await resend.Content.ReadAsStringAsync()).ToLowerInvariant().ShouldNotContain("token");
        var rotatedToken = fixture.LastInvitationToken(email);
        rotatedToken.ShouldNotBe(createdToken);

        (await fixture.AuthClient(TestData.NewSubject())
            .PostAsync(new Uri($"/api/v1/invitations/{createdToken}/accept", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Revoke removes it from the pending list.
        (await owner.PostAsync(new Uri($"/api/v1/workspaces/{ws.Id}/invitations/{row.Id}/revoke", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterRevoke = await owner.GetFromJsonAsync<List<PendingInvitationResponse>>($"/api/v1/workspaces/{ws.Id}/invitations");
        afterRevoke!.ShouldNotContain(p => p.Id == row.Id);
    }

    [Fact]
    public async Task Inviter_is_notified_when_invitation_is_accepted()
    {
        var (owner, _, ws) = await SetupAsync("invnotify");

        await fixture.InviteMemberAsync(owner, ws.Id, "notifyme");

        var notifications = await owner.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications?unreadOnly=true");
        notifications!.ShouldContain(n => n.EventType == "invitation.accepted" && n.EntityType == "Workspace" && n.EntityId == ws.Id);
    }

    [Fact]
    public async Task Member_cannot_change_roles()
    {
        var (owner, _, ws) = await SetupAsync("noperm");
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, ws.Id, "plain");
        var membershipId = await MembershipIdOfAsync(owner, ws.Id, memberUserId);

        var memberClient = fixture.AuthClient(memberSubject, ws.Id);
        var response = await memberClient.PatchAsJsonAsync($"/api/v1/workspaces/{ws.Id}/members/{membershipId}", new { role = "Owner" });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Accepting_with_mismatched_email_is_forbidden()
    {
        var (owner, _, ws) = await SetupAsync("emailguard");
        var invitedEmail = $"invited-{Guid.NewGuid():N}@planvexa.test";
        var invite = await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/invitations", new { email = invitedEmail, role = "Member" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var token = fixture.LastInvitationToken(invitedEmail);

        var response = await fixture.AuthClient(TestData.NewSubject())
            .PostAsync(new Uri($"/api/v1/invitations/{token}/accept", UriKind.Relative), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
