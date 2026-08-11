namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>
/// Workspace-only isolation at the API layer (AGENTS.md: "There is no Organization/Tenant layer";
/// Workspace is the single top-level authorization boundary â€ â€” see ADR 0015).
/// </summary>
[Collection("api")]
public sealed class TenantIsolationApiTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task User_cannot_access_a_workspace_they_are_not_a_member_of()
    {
        var slugA = TestData.NewSlug("orga");
        var (_, workspaceA) = await fixture.AuthClient(TestData.NewSubject()).RegisterOrgAsync(slugA);

        var outsider = TestData.NewSubject();
        await fixture.AuthClient(outsider).RegisterOrgAsync(TestData.NewSlug("orgb"));

        // Outsider is authenticated but is NOT a member of workspace A.
        var response = await fixture.AuthClient(outsider, workspaceA.Id)
            .GetAsync(new Uri("/api/v1/features", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Workspace_header_alone_can_scope_a_request()
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug("ws-only");
        var (reg, org) = await fixture.AuthClient(subject).RegisterOrgAsync(slug);
        reg.EnsureSuccessStatusCode();

        var client = fixture.AuthClient(subject);
        client.DefaultRequestHeaders.Add("X-Workspace", org.Id.ToString());

        var features = await client.GetFromJsonAsync<List<FeatureResponse>>("/api/v1/features");

        features!.ShouldContain(f => f.Key == "workspaces");
    }

    [Fact]
    public async Task Invitation_flow_admits_member_but_denies_member_management()
    {
        // Owner sets up the workspace.
        var ownerSubject = TestData.NewSubject();
        var slug = TestData.NewSlug("team");
        var (regResponse, mainWorkspace) = await fixture.AuthClient(ownerSubject).RegisterOrgAsync(slug);
        regResponse.EnsureSuccessStatusCode();

        var owner = fixture.AuthClient(ownerSubject, mainWorkspace.Id);

        // Owner invites a member.
        var inviteResponse = await owner.PostAsJsonAsync(
            $"/api/v1/workspaces/{mainWorkspace.Id}/invitations",
            new { email = "invitee@planvexa.test", role = "Member" });
        inviteResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await inviteResponse.Content.ReadAsStringAsync()).ToLowerInvariant().ShouldNotContain("token");
        var token = fixture.LastInvitationToken("invitee@planvexa.test");
        token.ShouldNotBeNullOrWhiteSpace();

        // Invitee accepts using the raw token from the invitation email.
        var inviteeSubject = TestData.NewSubject();
        var inviteeNoWorkspace = fixture.AuthClient(inviteeSubject);
        var acceptResponse = await inviteeNoWorkspace.PostAsync(
            new Uri($"/api/v1/invitations/{token}/accept", UriKind.Relative), content: null);
        acceptResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var accepted = await acceptResponse.Content.ReadFromJsonAsync<AcceptResponse>();
        accepted!.Role.ShouldBe("Member");

        // Invitee (Member) can read features...
        var invitee = fixture.AuthClient(inviteeSubject, mainWorkspace.Id);
        (await invitee.GetAsync(new Uri($"/api/v1/features?workspaceId={mainWorkspace.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // ...but a plain Member cannot invite other members (MembersInvite is Admin+ only).
        var inviteAttempt = await invitee.PostAsJsonAsync(
            $"/api/v1/workspaces/{mainWorkspace.Id}/invitations",
            new { email = "another@planvexa.test", role = "Member" });
        inviteAttempt.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Owner sees two members in the workspace.
        var members = await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{mainWorkspace.Id}/members");
        members!.Count.ShouldBe(2);
        members!.ShouldContain(m => m.Role == "Owner");
        members!.ShouldContain(m => m.Role == "Member");
    }

    [Fact]
    public async Task Workspace_header_for_an_inaccessible_workspace_is_rejected()
    {
        // Two entirely independent Workspaces, owned by two different users.
        var subjectA = TestData.NewSubject();
        var subjectB = TestData.NewSubject();
        var slugA = TestData.NewSlug("wha");
        var slugB = TestData.NewSlug("whb");
        var (regA, workspaceA) = await fixture.AuthClient(subjectA).RegisterOrgAsync(slugA);
        var (regB, workspaceB) = await fixture.AuthClient(subjectB).RegisterOrgAsync(slugB);
        regA.EnsureSuccessStatusCode();
        regB.EnsureSuccessStatusCode();

        // Subject A (a member only of workspace A) requests workspace B via the header.
        var client = fixture.AuthClient(subjectA);
        client.DefaultRequestHeaders.Add("X-Workspace", workspaceB.Id.ToString());

        var response = await client.GetAsync(new Uri("/api/v1/features", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _ = workspaceA;
    }

    [Fact]
    public async Task Malformed_workspace_header_is_rejected()
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug("whbad");
        await fixture.AuthClient(subject).RegisterOrgAsync(slug);

        var client = fixture.AuthClient(subject);
        client.DefaultRequestHeaders.Add("X-Workspace", "not-a-guid");

        var response = await client.GetAsync(new Uri("/api/v1/features", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Accepting_unknown_token_returns_not_found()
    {
        var client = fixture.AuthClient(TestData.NewSubject());
        var response = await client.PostAsync(
            new Uri($"/api/v1/invitations/{Guid.NewGuid():N}/accept", UriKind.Relative), content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
