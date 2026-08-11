namespace Planvexa.IntegrationTests;

using Npgsql;
using Planvexa.Database;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class DevelopmentSeedTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("planvexa")
        .WithUsername("planvexa")
        .WithPassword("planvexa")
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        PlanvexaDatabase.Upgrade(ConnectionString);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task FirstExecution_SeedsCompleteDemoWorkspaces()
    {
        await PlanvexaDevelopmentSeeder.SeedAsync(ConnectionString, seedDevelopmentData: true);

        (await CountAsync("identity.users")).ShouldBe(4);
        (await CountAsync("tenancy.workspaces WHERE slug IN ('product-operations', 'client-portal')")).ShouldBe(2);
        // Exact, not a floor: a first login must not look like an empty product, and the demo seed
        // is the only thing standing between a fresh database and three lonely tasks.
        (await CountAsync("work.tasks")).ShouldBe(18);
        (await CountAsync("work.task_assignees")).ShouldBe(17);
        // The E2E sandbox list ships empty — the write specs fill it and global-teardown drains it.
        (await CountAsync("work.tasks WHERE list_id = '018f0000-0000-7000-8000-000000012901'")).ShouldBe(0);
        (await CountAsync("collab.comments")).ShouldBeGreaterThanOrEqualTo(2);
        (await CountAsync("chat.messages")).ShouldBeGreaterThanOrEqualTo(2);
        (await CountAsync("time.time_entries")).ShouldBeGreaterThanOrEqualTo(1);
        (await CountAsync("planning.sprints")).ShouldBeGreaterThanOrEqualTo(1);
        (await CountAsync("docs.documents")).ShouldBeGreaterThanOrEqualTo(1);
        (await CountAsync("forms.forms")).ShouldBeGreaterThanOrEqualTo(1);
        (await CountAsync("automation.automation_rules")).ShouldBeGreaterThanOrEqualTo(1);
        (await CountAsync("integrations.personal_access_tokens")).ShouldBeGreaterThanOrEqualTo(1);
        (await CountAsync("governance.retention_policies")).ShouldBeGreaterThanOrEqualTo(1);
        (await CountAsync("ai.ai_requests")).ShouldBeGreaterThanOrEqualTo(1);
        (await CountAsync("mobile.device_registrations")).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task RepeatedExecution_IsIdempotent()
    {
        await PlanvexaDevelopmentSeeder.SeedAsync(ConnectionString, seedDevelopmentData: true);
        var firstUsers = await CountAsync("identity.users");
        var firstTasks = await CountAsync("work.tasks");
        var firstMessages = await CountAsync("chat.messages");

        await PlanvexaDevelopmentSeeder.SeedAsync(ConnectionString, seedDevelopmentData: true);

        (await CountAsync("identity.users")).ShouldBe(firstUsers);
        (await CountAsync("work.tasks")).ShouldBe(firstTasks);
        (await CountAsync("chat.messages")).ShouldBe(firstMessages);
    }

    [Fact]
    public async Task PartialExistingData_IsCompletedWithoutDuplicates()
    {
        await ExecuteAsync("""
            INSERT INTO identity.users (id, subject, email, display_name, is_active, created_at_utc)
            VALUES ('018f0000-0000-7000-8000-000000001001', 'dev-owner', 'old-owner@planvexa.local', 'Old Owner', true, '2026-01-01T00:00:00Z');
            """);

        await PlanvexaDevelopmentSeeder.SeedAsync(ConnectionString, seedDevelopmentData: true);

        (await CountAsync("identity.users WHERE subject = 'dev-owner'")).ShouldBe(1);
        (await ScalarStringAsync("SELECT email FROM identity.users WHERE subject = 'dev-owner';")).ShouldBe("owner@planvexa.local");
        (await CountAsync("tenancy.workspaces WHERE slug IN ('product-operations', 'client-portal')")).ShouldBe(2);
        (await CountAsync("work.tasks")).ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Reset_RemovesDemoData_AndAllowsReseed()
    {
        await PlanvexaDevelopmentSeeder.SeedAsync(ConnectionString, seedDevelopmentData: true);
        (await CountAsync("tenancy.workspaces WHERE slug IN ('product-operations', 'client-portal')")).ShouldBe(2);

        await PlanvexaDevelopmentSeeder.ResetAsync(ConnectionString);

        (await CountAsync("tenancy.workspaces WHERE slug IN ('product-operations', 'client-portal')")).ShouldBe(0);
        (await CountAsync("identity.users WHERE subject = 'dev-owner'")).ShouldBe(0);

        await PlanvexaDevelopmentSeeder.SeedAsync(ConnectionString, seedDevelopmentData: true);
        (await CountAsync("tenancy.workspaces WHERE slug IN ('product-operations', 'client-portal')")).ShouldBe(2);
    }
    [Fact]
    public async Task DisabledDevelopmentSeed_DoesNotInsertDemoData()
    {
        await PlanvexaDevelopmentSeeder.SeedAsync(ConnectionString, seedDevelopmentData: false);

        (await CountAsync("tenancy.workspaces WHERE slug IN ('product-operations', 'client-portal')")).ShouldBe(0);
    }

    private async Task<long> CountAsync(string fromClause)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {fromClause};";
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<string> ScalarStringAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

