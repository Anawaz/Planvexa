namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

[Collection("api")]
public sealed class ApiFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Register_workspace_creates_owner()
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug("acme");
        var client = fixture.AuthClient(subject);

        var (response, org) = await client.RegisterOrgAsync(slug, name: "Acme Inc");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        org.Slug.ShouldBe(slug);
        org.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Registering_duplicate_workspace_slug_returns_conflict()
    {
        var subject = TestData.NewSubject();
        var slug1 = TestData.NewSlug("dup1");
        var slug2 = TestData.NewSlug("dup2");

        // First user creates workspace
        var first = fixture.AuthClient(subject);
        var (response1, org1) = await first.RegisterOrgAsync(slug1);
        response1.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Slug is globally unique now (Workspace is the sole top-level concept) — even a brand-new
        // Workspace-creation request for the same slug should get conflict.
        var client = fixture.AuthClient(subject, org1.Id);
        var duplicateResponse = await client.PostAsJsonAsync("/api/v1/workspaces", new { name = "Duplicate", slug = slug1 });
        duplicateResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // But a different slug should work
        var validResponse = await client.PostAsJsonAsync("/api/v1/workspaces", new { name = "Valid", slug = slug2 });
        validResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Invalid_slug_returns_validation_problem()
    {
        var client = fixture.AuthClient(TestData.NewSubject());
        var response = await client.PostAsJsonAsync("/api/v1/workspaces", new
        {
            name = "Bad",
            slug = "Invalid Slug!",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Features_are_seeded_and_listed_for_workspace()
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug("feat");
        var (response, org) = await fixture.AuthClient(subject).RegisterOrgAsync(slug);
        response.EnsureSuccessStatusCode();

        var workspaceClient = fixture.AuthClient(subject, org.Id);
        var features = await workspaceClient.GetFromJsonAsync<List<FeatureResponse>>($"/api/v1/features?workspaceId={org.Id}");

        features.ShouldNotBeNull();
        features!.ShouldContain(f => f.Key == "workspaces");
        features!.ShouldContain(f => f.Key == "time_tracking" && f.Enabled);
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        var client = fixture.Factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Owner_can_create_additional_workspace()
    {
        var subject = TestData.NewSubject();
        var initialSlug = TestData.NewSlug("ws");
        var (regResponse, initial) = await fixture.AuthClient(subject).RegisterOrgAsync(initialSlug);
        regResponse.EnsureSuccessStatusCode();

        var client = fixture.AuthClient(subject, initial.Id);
        var engineeringSlug = TestData.NewSlug("eng");
        var response = await client.PostAsJsonAsync("/api/v1/workspaces", new { name = "Engineering", slug = engineeringSlug });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var workspaces = await client.GetFromJsonAsync<List<WorkspaceResponse>>("/api/v1/workspaces/mine");
        workspaces!.ShouldContain(w => w.Slug == engineeringSlug);
        workspaces!.ShouldContain(w => w.Slug == initialSlug); // initial workspace
    }
}
