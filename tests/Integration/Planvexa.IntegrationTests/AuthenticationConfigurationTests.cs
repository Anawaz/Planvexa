namespace Planvexa.IntegrationTests;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

[Collection("api")]
public sealed class AuthenticationConfigurationTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Development_headers_are_ignored_when_not_explicitly_enabled()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("ConnectionStrings:Planvexa", fixture.ConnectionString);
                builder.UseSetting("Database:RunDbUpOnStartup", "true");
                builder.UseSetting("Database:SeedDevelopmentData", "false");
                builder.UseSetting("Authentication:UseDevelopmentHeaders", "false");
                builder.UseSetting("OpenTelemetry:OtlpEndpoint", string.Empty);
            });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Subject", TestData.NewSubject());

        var response = await client.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Development_headers_work_when_explicitly_enabled()
    {
        var response = await fixture.AuthClient(TestData.NewSubject())
            .GetAsync(new Uri("/api/v1/users/me", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
