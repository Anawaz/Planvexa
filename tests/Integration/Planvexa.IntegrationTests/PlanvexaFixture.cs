namespace Planvexa.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Boots a real PostgreSQL 18 container (Testcontainers) and a <see cref="WebApplicationFactory{T}"/>
/// against it. DbUp scripts (including RLS) run on startup, so the schema under test is identical to
/// production. Shared by all integration tests via the "api" collection.
/// </summary>
public sealed class PlanvexaFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("planvexa")
        .WithUsername("planvexa")
        .WithPassword("planvexa")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Connection string for a dedicated NON-superuser role. The Testcontainers superuser bypasses
    /// RLS, so RLS behaviour can only be proven through a role that is subject to it.
    /// </summary>
    public string AppRoleConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Deploy the schema as the superuser (tables end up superuser-owned), then hand the API a
        // non-owner, NOBYPASSRLS role: exactly the production posture, where FORCE RLS applies to
        // every application query. Cross-tenant background work gets the superuser connection as the
        // maintenance connection, mirroring the privileged role a real deployment provisions.
        Planvexa.Database.PlanvexaDatabase.Upgrade(ConnectionString);
        AppRoleConnectionString = await CreateNonSuperuserRoleAsync();

        _factory = new PlanvexaApiFactory(AppRoleConnectionString, ConnectionString);

        using var client = _factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
    }

    private async Task<string> CreateNonSuperuserRoleAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var statements = new[]
        {
            "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'planvexa_app') THEN CREATE ROLE planvexa_app LOGIN PASSWORD 'app' NOSUPERUSER NOBYPASSRLS; END IF; END $$;",
            "GRANT USAGE ON SCHEMA tenancy, audit, identity, platform, work, collab, sharing, notifications, time, planning, reporting, docs, forms, automation, integrations, governance, ai, mobile, chat, goals, whiteboards, clips TO planvexa_app;",
            "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tenancy, audit, identity, platform, work, collab, sharing, notifications, time, planning, reporting, docs, forms, automation, integrations, governance, ai, mobile, chat, goals, whiteboards, clips TO planvexa_app;",
            "GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA tenancy, audit, identity, platform, work, collab, sharing, notifications, time, planning, reporting, docs, forms, automation, integrations, governance, ai, mobile, chat, goals, whiteboards, clips TO planvexa_app;",
        };

        foreach (var sql in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        // Connection pooling stays ON, exactly as in production. Disabling it opened a fresh TCP
        // connection for every EF query, and a full suite run exhausted the Windows ephemeral port
        // range (SocketException 10048 → sporadic 500s in unrelated tests). It is also safe:
        // WorkspaceConnectionInterceptor re-stamps app.current_workspace/app.current_user on every open
        // and clears them when there is no workspace, so a reused connection never inherits another
        // workspace's session state — which is the same guarantee production depends on.
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = "planvexa_app",
            Password = "app",
        };
        return builder.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    private sealed class PlanvexaApiFactory(string connectionString, string maintenanceConnectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Planvexa", connectionString);
            builder.UseSetting("ConnectionStrings:PlanvexaMaintenance", maintenanceConnectionString);
            builder.UseSetting("Database:RunDbUpOnStartup", "false");
            // Every test builds the workspaces it needs; the first-run bootstrap admin/workspace would
            // just be an extra row in every isolation and roster assertion. BootstrapSeedTests boots a
            // factory with it enabled and owns that coverage.
            builder.UseSetting("Bootstrap:Enabled", "false");
            builder.UseSetting("OpenTelemetry:OtlpEndpoint", string.Empty);
            builder.UseSetting("Authentication:UseDevelopmentHeaders", "true");
            // Pinned regardless of appsettings.json's value (developers toggle this locally to test the
            // invite-only flow) — almost every test provisions a fresh subject with no pending invitation,
            // so this fixture always needs self-registration allowed. RegistrationGateTests owns the
            // AllowSelfRegistration=false coverage with its own dedicated factory.
            builder.UseSetting("Registration:AllowSelfRegistration", "true");

            // Attachment bytes go to a throwaway directory instead of the API project's App_Data.
            builder.UseSetting(
                "FileStorage:RootPath",
                Path.Combine(Path.GetTempPath(), "planvexa-tests", Guid.NewGuid().ToString("N")));
        }
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<PlanvexaFixture>;

