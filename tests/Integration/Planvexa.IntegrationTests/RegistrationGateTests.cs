namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// <c>Registration:AllowSelfRegistration = false</c> — own container and factory rather than the shared
/// <see cref="PlanvexaFixture"/>, because the whole point is a config value the shared fixture doesn't
/// set. Bootstrap admin creation (<see cref="BootstrapSeedTests"/>) always bypasses this gate; that's
/// covered separately.
/// </summary>
public sealed class RegistrationGateTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("planvexa")
        .WithUsername("planvexa")
        .WithPassword("planvexa")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("Not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Planvexa.Database.PlanvexaDatabase.Upgrade(_container.GetConnectionString());
        _factory = new GatedApiFactory(_container.GetConnectionString());

        using var client = Factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    private HttpClient AuthClient(string subject)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Subject", subject);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{subject}@planvexa.test");
        return client;
    }

    [Fact]
    public async Task Brand_new_user_with_no_invitation_is_forbidden()
    {
        var response = await AuthClient(TestData.NewSubject()).GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Public_registration_policy_endpoint_reflects_the_configured_flag()
    {
        // Anonymous — no X-Debug-* headers — this is what the landing page reads pre-auth to decide
        // whether to show Sign up / Start onboarding at all.
        var response = await Factory.CreateClient().GetAsync(new Uri("/api/v1/public/registration-policy", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RegistrationPolicyResponse>();
        body!.AllowSelfRegistration.ShouldBeFalse();

        await using var allowedFactory = new GatedApiFactory(_container.GetConnectionString(), allowSelfRegistration: true);
        var allowedResponse = await allowedFactory.CreateClient().GetAsync(new Uri("/api/v1/public/registration-policy", UriKind.Relative));
        (await allowedResponse.Content.ReadFromJsonAsync<RegistrationPolicyResponse>())!.AllowSelfRegistration.ShouldBeTrue();
    }

    private sealed record RegistrationPolicyResponse(bool AllowSelfRegistration);

    [Fact]
    public async Task Brand_new_user_with_a_pending_invitation_can_still_be_provisioned()
    {
        // The owner is itself a brand-new user, so it must be seeded directly (an owner can't invite
        // anyone before it exists, and self-registration is off) rather than provisioned through the API.
        var ownerSubject = await SeedExistingUserAsync("owner");
        var owner = AuthClient(ownerSubject);
        var (regResponse, workspace) = await owner.RegisterOrgAsync(TestData.NewSlug("gate"));
        regResponse.EnsureSuccessStatusCode();
        owner.DefaultRequestHeaders.Add("X-Workspace", workspace.Id.ToString());

        var inviteeSubject = TestData.NewSubject();
        var inviteeEmail = $"{inviteeSubject}@planvexa.test";
        var invite = await owner.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/invitations", new { email = inviteeEmail, role = "Member" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The invitee's very first authenticated call is /users/me — no prior HTTP contact — proving the
        // gate itself (not just the invitations endpoint) admits it because a pending invitation exists.
        var invitee = AuthClient(inviteeSubject);
        var me = await invitee.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<string> SeedExistingUserAsync(string prefix)
    {
        var subject = $"{prefix}-{Guid.NewGuid():N}";
        using var scope = Factory.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<Planvexa.SharedContracts.Users.IUserDirectory>();
        await directory.GetOrProvisionAsync(subject, $"{subject}@planvexa.test", subject, enforceRegistrationGate: false);
        return subject;
    }

    private sealed class GatedApiFactory(string connectionString, bool allowSelfRegistration = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Planvexa", connectionString);
            builder.UseSetting("ConnectionStrings:PlanvexaMaintenance", connectionString);
            builder.UseSetting("Database:RunDbUpOnStartup", "false");
            builder.UseSetting("Bootstrap:Enabled", "false");
            builder.UseSetting("OpenTelemetry:OtlpEndpoint", string.Empty);
            builder.UseSetting("Authentication:UseDevelopmentHeaders", "true");
            builder.UseSetting("Registration:AllowSelfRegistration", allowSelfRegistration ? "true" : "false");
            builder.UseSetting(
                "FileStorage:RootPath",
                Path.Combine(Path.GetTempPath(), "planvexa-tests", Guid.NewGuid().ToString("N")));
        }
    }
}
