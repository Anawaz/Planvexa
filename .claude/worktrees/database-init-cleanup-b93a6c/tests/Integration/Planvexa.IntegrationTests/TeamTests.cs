namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>Teams — create/list, membership management, and permission enforcement.</summary>
[Collection("api")]
public sealed class TeamTests(PlanvexaFixture fixture)
{
    private sealed record TeamResp(Guid Id, Guid WorkspaceId, string Name, string? Description, bool IsArchived, int MemberCount);
    private sealed record TeamMemberResp(Guid UserId, DateTimeOffset AddedAtUtc);

    private async Task<(HttpClient Owner, string Subject, WorkspaceResponse Workspace)> SetupAsync(string prefix)
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug(prefix);
        var (response, org) = await fixture.AuthClient(subject).RegisterOrgAsync(slug);
        response.EnsureSuccessStatusCode();

        var owner = fixture.AuthClient(subject, org.Id);
        return (owner, subject, org);
    }

    [Fact]
    public async Task Owner_creates_a_team_and_manages_its_members()
    {
        var (owner, _, ws) = await SetupAsync("team");
        var create = await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/teams", new { name = "Engineering", description = "Builders" });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var team = (await create.Content.ReadFromJsonAsync<TeamResp>())!;
        team.Name.ShouldBe("Engineering");
        team.MemberCount.ShouldBe(0);

        var me = await owner.GetFromJsonAsync<CurrentUserResponse>("/api/v1/users/me");
        (await owner.PostAsJsonAsync($"/api/v1/teams/{team.Id}/members", new { userId = me!.UserId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var members = await owner.GetFromJsonAsync<List<TeamMemberResp>>($"/api/v1/teams/{team.Id}/members");
        members!.ShouldContain(m => m.UserId == me.UserId);

        var listed = await owner.GetFromJsonAsync<List<TeamResp>>($"/api/v1/workspaces/{ws.Id}/teams");
        listed!.Single(t => t.Id == team.Id).MemberCount.ShouldBe(1);

        (await owner.DeleteAsync(new Uri($"/api/v1/teams/{team.Id}/members/{me.UserId}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterRemove = await owner.GetFromJsonAsync<List<TeamMemberResp>>($"/api/v1/teams/{team.Id}/members");
        afterRemove!.ShouldNotContain(m => m.UserId == me.UserId);
    }

    [Fact]
    public async Task Adding_a_non_member_user_is_rejected()
    {
        var (owner, _, ws) = await SetupAsync("teamx");
        var create = await owner.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/teams", new { name = "Ops", description = (string?)null });
        var team = (await create.Content.ReadFromJsonAsync<TeamResp>())!;

        (await owner.PostAsJsonAsync($"/api/v1/teams/{team.Id}/members", new { userId = Guid.NewGuid() }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_plain_member_cannot_create_a_team()
    {
        var (owner, _, ws) = await SetupAsync("teamperm");
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, ws.Id, "tm");
        var member = fixture.AuthClient(memberSubject, ws.Id);

        (await member.PostAsJsonAsync($"/api/v1/workspaces/{ws.Id}/teams", new { name = "Nope", description = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
