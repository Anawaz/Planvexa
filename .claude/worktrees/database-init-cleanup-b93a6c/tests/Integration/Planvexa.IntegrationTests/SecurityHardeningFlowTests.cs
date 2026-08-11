namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Planvexa.Api.Storage;
using Shouldly;
using Testcontainers.Minio;
using Xunit;

internal sealed record IpAllowRuleResp(Guid Id, string Cidr, string? Description, DateTimeOffset CreatedAtUtc);

/// <summary>
/// TestServer's in-memory transport leaves <c>HttpContext.Connection.RemoteIpAddress</c> null (there is no
/// real socket) — production Kestrel always populates it, so <see cref="IpAllowListMiddleware"/> itself
/// needs no test-only carve-out, but exercising its blocking behaviour here needs a way to simulate a
/// caller IP. An <see cref="IStartupFilter"/> wraps a middleware around the front of the pipeline Program.cs
/// already built (it composes with that pipeline, it does not replace it, unlike <c>IWebHostBuilder.Configure</c>)
/// that sets the connection's remote address from a test-only header — nothing in production code changes.
/// </summary>
internal sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var value) && IPAddress.TryParse(value.ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            await nextMiddleware();
        });
        next(app);
    };
}

/// <summary>
/// Security and platform hardening: security response headers present on real responses (item
/// 2) and the IP allow-list middleware actually blocking/allowing requests by source IP (item 8).
/// </summary>
[Collection("api")]
public sealed class SecurityHardeningFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Every_response_carries_the_baseline_security_headers()
    {
        var client = fixture.Factory.CreateDefaultClient();
        var response = await client.GetAsync("/health/live");

        response.Headers.TryGetValues("X-Content-Type-Options", out var contentTypeOptions).ShouldBeTrue();
        contentTypeOptions!.ShouldContain("nosniff");

        response.Headers.TryGetValues("X-Frame-Options", out var frameOptions).ShouldBeTrue();
        frameOptions!.ShouldContain("DENY");

        response.Headers.TryGetValues("Referrer-Policy", out var referrerPolicy).ShouldBeTrue();
        referrerPolicy!.ShouldContain("strict-origin-when-cross-origin");

        response.Headers.TryGetValues("Content-Security-Policy", out var csp).ShouldBeTrue();
        csp!.Single().ShouldContain("default-src 'none'");
    }

    [Fact]
    public async Task Scalar_and_openapi_routes_are_exempt_from_the_json_api_csp()
    {
        // The API's CSP is default-src 'none' (a JSON API never needs to load anything of its own), which
        // would break the dev-only Scalar UI's own script/style bundle — see SecurityHeadersMiddleware's
        // doc comment.
        var client = fixture.Factory.CreateDefaultClient();
        var response = await client.GetAsync("/openapi/v1.json");
        response.Headers.TryGetValues("Content-Security-Policy", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Ip_allow_list_blocks_a_non_matching_source_and_allows_a_matching_one()
    {
        const string simulatedCallerIp = "198.51.100.7"; // TEST-NET-2 (RFC 5737), never a real caller.

        await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, TestRemoteIpStartupFilter>()));

        var subject = TestData.NewSubject();
        var bootstrap = factory.CreateDefaultClient();
        bootstrap.DefaultRequestHeaders.Add("X-Debug-Subject", subject);
        bootstrap.DefaultRequestHeaders.Add("X-Debug-Email", $"{subject}@planvexa.test");
        bootstrap.DefaultRequestHeaders.Add(TestRemoteIpStartupFilter.HeaderName, simulatedCallerIp);
        var slug = TestData.NewSlug("ipallow");
        var createWorkspace = await bootstrap.PostAsJsonAsync("/api/v1/workspaces", new { name = slug, slug });
        createWorkspace.EnsureSuccessStatusCode();
        var workspace = (await createWorkspace.Content.ReadFromJsonAsync<WorkspaceResponse>())!;

        var client = factory.CreateDefaultClient();
        client.DefaultRequestHeaders.Add("X-Debug-Subject", subject);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{subject}@planvexa.test");
        client.DefaultRequestHeaders.Add("X-Workspace", workspace.Id.ToString());
        client.DefaultRequestHeaders.Add(TestRemoteIpStartupFilter.HeaderName, simulatedCallerIp);

        // Sanity: unrestricted (no rules yet).
        (await client.GetAsync("/api/v1/spaces")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var nonMatching = await client.PostAsJsonAsync("/api/v1/governance/ip-allow-rules", new { cidr = "203.0.113.0/24", description = "not this host" });
        nonMatching.StatusCode.ShouldBe(HttpStatusCode.Created);

        var blocked = await client.GetAsync("/api/v1/spaces");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Adding a rule that DOES cover the caller restores access — a workspace can combine multiple
        // ranges (this is an allow LIST, not a single value).
        var matching = await client.PostAsJsonAsync("/api/v1/governance/ip-allow-rules", new { cidr = "198.51.100.0/24", description = "this test's simulated caller" });
        matching.StatusCode.ShouldBe(HttpStatusCode.Created);
        var matchingRule = (await matching.Content.ReadFromJsonAsync<IpAllowRuleResp>())!;

        (await client.GetAsync("/api/v1/spaces")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Removing every rule returns the workspace to unrestricted.
        var listBeforeCleanup = await client.GetFromJsonAsync<List<IpAllowRuleResp>>("/api/v1/governance/ip-allow-rules");
        foreach (var rule in listBeforeCleanup!)
        {
            (await client.DeleteAsync($"/api/v1/governance/ip-allow-rules/{rule.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await client.GetAsync("/api/v1/spaces")).StatusCode.ShouldBe(HttpStatusCode.OK);
        matchingRule.Cidr.ShouldBe("198.51.100.0/24");
    }

    [Fact]
    public async Task A_non_admin_cannot_manage_the_ip_allow_list()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "ipallow");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var attempt = await member.PostAsJsonAsync("/api/v1/governance/ip-allow-rules", new { cidr = "203.0.113.0/24", description = (string?)null });
        attempt.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

/// <summary>
/// S3-compatible object storage against a real MinIO container (Testcontainers), the same
/// class of test <c>PlanvexaFixture</c> already runs for PostgreSQL. Exercises <see cref="S3FileStorage"/>
/// directly rather than through the full API host, since <c>PlanvexaFixture</c>'s <c>WebApplicationFactory</c>
/// is fixed to local-disk storage — this is a focused unit-of-infrastructure test for the IFileStorage
/// implementation itself, not a full request/response flow.
/// </summary>
public sealed class S3FileStorageMinioTests : IAsyncLifetime
{
    // Same image tag as docker-compose.yml/AppHost.cs's MinIO container.
    private readonly MinioContainer _minio = new MinioBuilder("minio/minio:RELEASE.2025-04-08T15-41-24Z").Build();

    public async ValueTask InitializeAsync() => await _minio.StartAsync();

    public async ValueTask DisposeAsync() => await _minio.DisposeAsync();

    private S3FileStorage BuildStorage()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:S3:ServiceUrl"] = _minio.GetConnectionString(),
                ["FileStorage:S3:BucketName"] = "planvexa-test",
                ["FileStorage:S3:AccessKey"] = _minio.GetAccessKey(),
                ["FileStorage:S3:SecretKey"] = _minio.GetSecretKey(),
                ["FileStorage:S3:ForcePathStyle"] = "true",
            })
            .Build();
        return new S3FileStorage(config);
    }

    [Fact]
    public async Task Save_open_and_delete_round_trip_through_a_real_minio_bucket()
    {
        var storage = BuildStorage();
        var path = $"workspaces/{Guid.NewGuid()}/attachments/{Guid.NewGuid()}/notes.txt";
        var content = "S3-compatible storage round trip"u8.ToArray();

        // Also proves the bucket-auto-create-on-first-use path (S3FileStorage.EnsureBucketAsync) — MinIO
        // starts with no buckets at all.
        await storage.SaveAsync(path, new MemoryStream(content));

        await using (var read = await storage.OpenReadAsync(path))
        {
            using var buffer = new MemoryStream();
            await read.CopyToAsync(buffer);
            buffer.ToArray().ShouldBe(content);
        }

        await storage.DeleteAsync(path);
        await Should.ThrowAsync<Exception>(() => storage.OpenReadAsync(path));
    }

    [Fact]
    public async Task Signed_urls_are_generated_with_the_expected_verb_and_bucket()
    {
        var storage = BuildStorage();
        var path = $"workspaces/{Guid.NewGuid()}/attachments/{Guid.NewGuid()}/report.pdf";

        var downloadUrl = await storage.GetSignedDownloadUrlAsync(path, TimeSpan.FromMinutes(5));
        downloadUrl.ShouldContain("planvexa-test");
        downloadUrl.ShouldContain(Uri.EscapeDataString(path).Replace("%2F", "/"), Case.Insensitive);

        var uploadUrl = await storage.GetSignedUploadUrlAsync(path, "application/pdf", TimeSpan.FromMinutes(5));
        uploadUrl.ShouldContain("planvexa-test");
        uploadUrl.ShouldNotBe(downloadUrl);
    }
}
