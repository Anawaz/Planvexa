namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

[Collection("api")]
public sealed class WorkspacesMineTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Onboarding_creates_an_independent_workspace_with_starter_structure_and_owner()
    {
        // First-run: a brand-new identity with no Organization and no ambient context.
        var subject = TestData.NewSubject();
        var bootstrap = fixture.AuthClient(subject);
        var slug = TestData.NewSlug("product-ops");

        // Direct Workspace onboarding — the user names only a Workspace; there is no Organization step.
        var created = await bootstrap.PostAsJsonAsync("/api/v1/workspaces", new { name = "Product Ops", slug });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var workspace = await created.Content.ReadFromJsonAsync<WorkspaceResponse>();
        workspace!.Name.ShouldBe("Product Ops");
        workspace!.Slug.ShouldBe(slug);

        // After onboarding, the workspace has its starter structure provisioned.
        var wsClient = fixture.WorkClient(subject, workspace.Id);
        var spaces = await wsClient.GetFromJsonAsync<List<SpaceResp>>("/api/v1/spaces");
        spaces!.ShouldContain(s => s.Name == "General");
    }

    [Fact]
    public async Task Mine_returns_only_workspaces_the_caller_is_a_member_of()
    {
        var ownerSubject = TestData.NewSubject();
        var slug = TestData.NewSlug("mine");
        var (response, org) = await fixture.AuthClient(ownerSubject).RegisterOrgAsync(slug);
        response.EnsureSuccessStatusCode();

        var engineeringSlug = TestData.NewSlug("eng");
        var owner = fixture.AuthClient(ownerSubject, org.Id);
        (await owner.PostAsJsonAsync("/api/v1/workspaces", new { name = "Engineering", slug = engineeringSlug }))
            .EnsureSuccessStatusCode();

        // The owner created both workspaces, so both are theirs.
        var ownerMine = await owner.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces/mine");
        ownerMine!.Select(w => w.Slug).ShouldBe([org.Slug, engineeringSlug], ignoreOrder: true);

        // A member invited to the first workspace only sees it.
        var mainId = ownerMine!.Single(w => w.Slug == org.Slug).Id;
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, mainId, "mine-member");

        var member = fixture.AuthClient(memberSubject, mainId);
        var memberMine = await member.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces/mine");
        memberMine!.Select(w => w.Slug).ShouldBe([org.Slug]);
    }

    [Fact]
    public async Task Members_listing_includes_display_name_and_email()
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug("dir");
        var (_, org) = await fixture.AuthClient(subject).RegisterOrgAsync(slug);

        var client = fixture.AuthClient(subject, org.Id);
        var workspaces = await client.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces/mine");
        var members = await client.GetFromJsonAsync<List<MemberDirectoryResponse>>(
            $"/api/v1/workspaces/{workspaces!.Single().Id}/members");

        members!.ShouldHaveSingleItem();
        members[0].Email.ShouldBe($"{subject}@planvexa.test");
        members[0].DisplayName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Users_me_returns_internal_user_id_before_a_workspace_is_selected()
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug("usersme");
        var (_, org) = await fixture.AuthClient(subject).RegisterOrgAsync(slug);

        // Bootstrap call: no X-Workspace header.
        var me = await fixture.AuthClient(subject).GetFromJsonAsync<CurrentUserResponse>("/api/v1/users/me");
        me!.UserId.ShouldNotBe(Guid.Empty);
        me.Email.ShouldBe($"{subject}@planvexa.test");
        me.DisplayName.ShouldNotBeNullOrWhiteSpace();

        // The returned id is the same internal id the members directory uses (no email matching).
        var client = fixture.AuthClient(subject, org.Id);
        var workspaces = await client.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces/mine");
        var members = await client.GetFromJsonAsync<List<MemberDirectoryResponse>>(
            $"/api/v1/workspaces/{workspaces!.Single().Id}/members");
        members!.Single().UserId.ShouldBe(me.UserId);
    }

    [Fact]
    public async Task Workspaces_me_returns_the_callers_memberships()
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug("wsme");
        var (_, org) = await fixture.AuthClient(subject).RegisterOrgAsync(slug);
        var client = fixture.AuthClient(subject, org.Id);

        var mine = await client.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces/mine");
        var me = await client.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces/me");

        me!.ShouldNotBeEmpty();
        me.Select(w => w.Id).ShouldBe(mine!.Select(w => w.Id), ignoreOrder: true);
    }

    [Fact]
    public async Task Workspaces_mine_flattens_every_membership_the_caller_has()
    {
        // One identity that owns two entirely independent Workspaces (there is no Organization/Tenant
        // layer any more — see AGENTS.md — so "all my workspaces" is just every active membership).
        var subject = TestData.NewSubject();
        var slugA = TestData.NewSlug("flata");
        var slugB = TestData.NewSlug("flatb");
        await fixture.AuthClient(subject).RegisterOrgAsync(slugA, workspaceName: "Alpha");
        await fixture.AuthClient(subject).RegisterOrgAsync(slugB, workspaceName: "Beta");

        // Bootstrap call — no X-Workspace: every workspace the caller belongs to is returned.
        var mine = (await fixture.AuthClient(subject)
            .GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces/mine"))!;

        mine.Select(w => w.Name).ShouldContain("Alpha");
        mine.Select(w => w.Name).ShouldContain("Beta");
    }

    [Fact]
    public async Task Creating_a_workspace_provisions_a_starter_space_and_list()
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug("prov");
        var (_, org) = await fixture.AuthClient(subject).RegisterOrgAsync(slug);
        var client = fixture.AuthClient(subject, org.Id);

        var created = await client.PostAsJsonAsync("/api/v1/workspaces", new { name = "Engineering", slug = TestData.NewSlug("eng") });
        created.EnsureSuccessStatusCode();
        var workspace = await created.Content.ReadFromJsonAsync<WorkspaceResponse>();

        var wsClient = fixture.WorkClient(subject, workspace!.Id);

        var spaces = await wsClient.GetFromJsonAsync<List<SpaceResp>>("/api/v1/spaces");
        var general = spaces!.SingleOrDefault(s => s.Name == "General");
        general.ShouldNotBeNull();

        var lists = await wsClient.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{general!.Id}/lists");
        lists!.Select(l => l.Name).ShouldContain("Tasks");

        var schemes = await wsClient.GetSchemesAsync();
        schemes.ShouldNotBeEmpty();
    }
}

internal sealed record CurrentUserResponse(Guid UserId, string Email, string DisplayName);

internal sealed record MemberDirectoryResponse(
    Guid Id, Guid UserId, string Role, string Status, bool IsGuest, string? DisplayName, string? Email);
