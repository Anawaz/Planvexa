namespace Planvexa.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// The first-run bootstrap (<c>PlanvexaBootstrap</c>): a database that has the schema but no rows must
/// still come up as a usable install, on any environment. Own container and factory rather than the
/// shared <see cref="PlanvexaFixture"/>, because the whole point is a genuinely empty database — the
/// shared fixture disables the bootstrap so every other test starts from zero workspaces.
/// </summary>
public sealed class BootstrapSeedTests : IAsyncLifetime
{
    private const string Subject = "bootstrap-admin-test";
    private const string Email = "bootstrap-admin@planvexa.test";
    private const string WorkspaceName = "Bootstrap Workspace";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("planvexa")
        .WithUsername("planvexa")
        .WithPassword("planvexa")
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Planvexa.Database.PlanvexaDatabase.Upgrade(ConnectionString);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task First_start_against_an_empty_database_creates_one_admin_and_one_usable_workspace()
    {
        await StartApiAsync();

        // The admin plus the workspace it owns — and nothing else. A production install must not get
        // the demo seed's four accounts or its sample content.
        (await CountAsync("identity.users")).ShouldBe(1);
        (await CountAsync($"identity.users WHERE subject = '{Subject}' AND email = '{Email}'")).ShouldBe(1);
        (await CountAsync("tenancy.workspaces")).ShouldBe(1);
        (await CountAsync($"tenancy.workspaces WHERE name = '{WorkspaceName}'")).ShouldBe(1);

        // Owner membership, the five built-in roles with their permission grants, and plan entitlements.
        (await CountAsync("tenancy.workspace_members WHERE role = 'Owner' AND status = 'Active'")).ShouldBe(1);
        (await CountAsync("tenancy.roles WHERE is_built_in = true")).ShouldBe(5);
        (await CountAsync("tenancy.role_permissions")).ShouldBeGreaterThan(0);
        (await CountAsync("tenancy.feature_entitlements")).ShouldBeGreaterThan(0);

        // The starter structure a workspace needs to be usable at all: WorkspaceDefaultsProvisioner's
        // default status scheme + General space + Tasks list.
        (await CountAsync("work.status_schemes WHERE is_default = true")).ShouldBe(1);
        (await CountAsync("work.statuses")).ShouldBeGreaterThan(0);
        (await CountAsync("work.spaces")).ShouldBe(1);
        (await CountAsync("work.lists")).ShouldBe(1);

        // The membership must be linked to the Owner role row, not just the enum — RoleId is the
        // authorization source of truth.
        (await CountAsync("""
            tenancy.workspace_members m
            JOIN tenancy.roles r ON r.id = m.role_id
            WHERE r.key = 'owner'
            """)).ShouldBe(1);
    }

    [Fact]
    public async Task Restarting_does_not_create_a_second_workspace()
    {
        await StartApiAsync();
        await StartApiAsync();
        await StartApiAsync();

        // Hardened RLS returns no rows without an ambient workspace, so a bootstrap that skipped
        // setting app.current_user would see "no workspaces" every time and create one per start.
        (await CountAsync("tenancy.workspaces")).ShouldBe(1);
        (await CountAsync("identity.users")).ShouldBe(1);
        (await CountAsync("work.spaces")).ShouldBe(1);
    }

    [Fact]
    public async Task Bootstrap_can_be_turned_off()
    {
        await StartApiAsync(bootstrapEnabled: false);

        (await CountAsync("identity.users")).ShouldBe(0);
        (await CountAsync("tenancy.workspaces")).ShouldBe(0);
    }

    [Fact]
    public async Task The_bootstrap_admin_becomes_the_first_host_administrator()
    {
        await StartApiAsync();

        (await CountAsync($"identity.users WHERE subject = '{Subject}' AND is_host_admin")).ShouldBe(1);
        (await CountAsync("identity.users WHERE is_host_admin")).ShouldBe(1);
    }

    [Fact]
    public async Task An_installation_left_with_no_host_administrator_recovers_on_restart()
    {
        await StartApiAsync();
        // Zero host administrators is unreachable through the console — it refuses to demote or disable
        // the last one — so this state only arises from a direct database edit or a lost account. That
        // is the lockout case, and a restart healing it is more useful than requiring config surgery.
        await ExecuteAsync("UPDATE identity.users SET is_host_admin = false;");

        await StartApiAsync();

        (await CountAsync($"identity.users WHERE subject = '{Subject}' AND is_host_admin")).ShouldBe(1);
    }

    [Fact]
    public async Task An_existing_host_administrator_is_never_added_to_by_a_restart()
    {
        await StartApiAsync();
        await ExecuteAsync($"UPDATE identity.users SET is_host_admin = false WHERE subject = '{Subject}';");
        // Somebody else now holds it — the normal steady state after the bootstrap account hands over.
        await ExecuteAsync($"""
            INSERT INTO identity.users (id, subject, email, display_name, is_active, is_host_admin, created_at_utc, has_custom_display_name, is_anonymized)
            VALUES (gen_random_uuid(), 'handover-admin', 'handover@planvexa.test', 'Handover', true, true, now(), false, false);
            """);

        await StartApiAsync();
        await StartApiAsync();

        // The bootstrap account stays demoted: while ANY host administrator exists, the grant is
        // self-administered and the bootstrap keeps its hands off.
        (await CountAsync($"identity.users WHERE subject = '{Subject}' AND is_host_admin")).ShouldBe(0);
        (await CountAsync("identity.users WHERE is_host_admin")).ShouldBe(1);
    }

    /// <summary>
    /// The demo seed writes its users directly in SQL and knows nothing about host administration, and
    /// it makes the bootstrap return early — so without an explicit grant on that path a seeded
    /// database (every local development environment) would have a console nobody can open.
    /// </summary>
    [Fact]
    public async Task A_demo_seeded_database_still_gets_a_host_administrator()
    {
        await Planvexa.Database.PlanvexaDevelopmentSeeder.SeedAsync(ConnectionString, seedDevelopmentData: true);

        await StartApiAsync(seedDevelopmentData: true, adminEmail: "admin@planvexa.local");

        // The seed's dev-admin holds that email and is promoted in place.
        (await CountAsync("identity.users WHERE email = 'admin@planvexa.local' AND is_host_admin")).ShouldBe(1);
        (await CountAsync("identity.users WHERE is_host_admin")).ShouldBe(1);

        // Adopted by email, NOT re-provisioned: its identity-provider subject must still be dev-admin,
        // or that login breaks.
        (await CountAsync("identity.users WHERE email = 'admin@planvexa.local' AND subject = 'dev-admin'")).ShouldBe(1);

        // And the bootstrap still did not create its own admin or workspace on top of the seed.
        (await CountAsync("identity.users")).ShouldBe(4);
    }

    [Fact]
    public async Task A_demo_seeded_database_grants_nothing_when_no_account_holds_the_configured_admin_email()
    {
        await Planvexa.Database.PlanvexaDevelopmentSeeder.SeedAsync(ConnectionString, seedDevelopmentData: true);

        await StartApiAsync(seedDevelopmentData: true, adminEmail: "nobody@planvexa.test");

        // Better a closed console than silently promoting whichever account happened to be first.
        (await CountAsync("identity.users WHERE is_host_admin")).ShouldBe(0);
        (await CountAsync("identity.users")).ShouldBe(4);
    }

    /// <summary>
    /// Boots the API the way a fresh install does — schema already deployed, demo seed off — and
    /// disposes it again, so a second call is a genuine restart against the same database.
    /// </summary>
    private async Task StartApiAsync(
        bool bootstrapEnabled = true, bool seedDevelopmentData = false, string adminEmail = Email)
    {
        await using var factory = new BootstrapApiFactory(
            ConnectionString, bootstrapEnabled, seedDevelopmentData, adminEmail);
        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
    }

    private async Task<int> CountAsync(string fromClause)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {fromClause};";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class BootstrapApiFactory(
        string connectionString, bool bootstrapEnabled, bool seedDevelopmentData, string adminEmail)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Planvexa", connectionString);
            builder.UseSetting("ConnectionStrings:PlanvexaMaintenance", connectionString);
            builder.UseSetting("Database:RunDbUpOnStartup", "false");
            builder.UseSetting("Database:SeedDevelopmentData", seedDevelopmentData ? "true" : "false");
            builder.UseSetting("Bootstrap:Enabled", bootstrapEnabled ? "true" : "false");
            builder.UseSetting("Bootstrap:AdminSubject", Subject);
            builder.UseSetting("Bootstrap:AdminEmail", adminEmail);
            builder.UseSetting("Bootstrap:WorkspaceName", WorkspaceName);
            builder.UseSetting("OpenTelemetry:OtlpEndpoint", string.Empty);
            builder.UseSetting("Authentication:UseDevelopmentHeaders", "true");
            builder.UseSetting(
                "FileStorage:RootPath",
                Path.Combine(Path.GetTempPath(), "planvexa-tests", Guid.NewGuid().ToString("N")));
        }
    }
}
