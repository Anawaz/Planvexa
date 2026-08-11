namespace Planvexa.IntegrationTests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.Tenancy.Domain;
using Shouldly;
using Xunit;

/// <summary>
/// Proves Workspace-only isolation holds at both the EF query-filter layer and the database RLS layer
/// (AGENTS.md: "Workspace is the single top-level business, authorization... boundary"; the legacy
/// Tenant layer has been fully removed � — see ADR 0015). tenancy.workspaces itself is
/// readable only via the user-scoped bootstrap_workspace_read RLS policy (0026) — there is no more
/// tenant-wide visibility to fall back on — so every seeded workspace here also gets a real Owner
/// membership row, and reads impersonate that Owner via app.current_user, exactly like production
/// requests do (UserContextMiddleware sets it before WorkspaceResolutionMiddleware runs).
/// </summary>
[Collection("api")]
public sealed class TenantIsolationDbTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Query_filter_scopes_reads_to_the_ambient_workspace()
    {
        var (workspaceA, ownerA) = Seed(TestData.NewSlug("qa"));
        var (workspaceB, _) = Seed(TestData.NewSlug("qb"));

        using var scope = fixture.Factory.Services.CreateScope();
        SetAmbient(scope, workspaceA, ownerA);
        var db = scope.ServiceProvider.GetRequiredService<PlanvexaDbContext>();

        var workspaces = await db.Workspaces.ToListAsync();

        // Workspace itself is not filtered by the ambient workspace query filter (it is the top-level
        // collection, addressed by id/bootstrap membership) — RLS (bootstrap_workspace_read) is what
        // actually scopes this read to workspaces ownerA belongs to.
        workspaces.ShouldContain(w => w.Id == workspaceA);
        workspaces.ShouldNotContain(w => w.Id == workspaceB);
    }

    [Fact]
    public async Task Row_level_security_isolates_via_a_non_superuser_role()
    {
        var (workspaceA, _) = Seed(TestData.NewSlug("ra"));
        var (workspaceB, ownerB) = Seed(TestData.NewSlug("rb"));

        // Connect as the NON-superuser role so RLS is actually enforced (superusers bypass it).
        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();

        await SetWorkspaceGucAsync(connection, workspaceB, ownerB);
        var visible = await ReadWorkspaceIdsAsync(connection);

        visible.ShouldContain(workspaceB);
        visible.ShouldNotContain(workspaceA);
        visible.ShouldAllBe(id => id == workspaceB);
    }

    [Fact]
    public async Task Row_level_security_returns_nothing_for_a_user_with_no_memberships()
    {
        // tenancy.workspaces visibility is scoped by membership (bootstrap_workspace_read), not by
        // the ambient app.current_workspace GUC — a stranger sees nothing regardless of which
        // workspace id is set.
        var (workspaceId, _) = Seed(TestData.NewSlug("rc"));

        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();

        await SetWorkspaceGucAsync(connection, workspaceId, Guid.CreateVersion7());
        var visible = await ReadWorkspaceIdsAsync(connection);

        visible.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_workspace_context_reads_no_workspace_rows_through_application_role()
    {
        Seed(TestData.NewSlug("mt"));

        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();

        await ClearWorkspaceGucAsync(connection);
        var visible = await ReadWorkspaceIdsAsync(connection);

        visible.ShouldBeEmpty();
    }

    [Fact]
    public async Task Application_role_is_not_superuser_and_cannot_bypass_rls()
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = 'planvexa_app';";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetBoolean(0).ShouldBeFalse();
        reader.GetBoolean(1).ShouldBeFalse();
    }

    [Fact]
    public async Task Clearing_workspace_context_prevents_connection_pool_leakage()
    {
        var (workspaceA, ownerA) = Seed(TestData.NewSlug("pa"));
        var (workspaceB, ownerB) = Seed(TestData.NewSlug("pb"));
        var pooled = new Npgsql.NpgsqlConnectionStringBuilder(fixture.AppRoleConnectionString)
        {
            Pooling = true,
            MaxPoolSize = 1,
        }.ConnectionString;

        await using (var first = new Npgsql.NpgsqlConnection(pooled))
        {
            await first.OpenAsync();
            await SetWorkspaceGucAsync(first, workspaceA, ownerA);
            var visible = await ReadWorkspaceIdsAsync(first);
            visible.ShouldAllBe(id => id == workspaceA);
        }

        await using (var second = new Npgsql.NpgsqlConnection(pooled))
        {
            await second.OpenAsync();
            await ClearWorkspaceGucAsync(second);
            var visible = await ReadWorkspaceIdsAsync(second);
            visible.ShouldBeEmpty();
        }

        await using (var third = new Npgsql.NpgsqlConnection(pooled))
        {
            await third.OpenAsync();
            await SetWorkspaceGucAsync(third, workspaceB, ownerB);
            var visible = await ReadWorkspaceIdsAsync(third);
            visible.ShouldAllBe(id => id == workspaceB);
        }

        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    private static async Task SetWorkspaceGucAsync(Npgsql.NpgsqlConnection connection, Guid workspaceId, Guid userId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT set_config('app.current_workspace', @workspace, false), set_config('app.current_user', @user, false)";
        command.Parameters.AddWithValue("workspace", workspaceId.ToString());
        command.Parameters.AddWithValue("user", userId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ClearWorkspaceGucAsync(Npgsql.NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_workspace', '', false), set_config('app.current_user', '', false)";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<Guid>> ReadWorkspaceIdsAsync(Npgsql.NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM tenancy.workspaces";
        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetGuid(0));
        }

        return result;
    }

    [Fact]
    public async Task Writing_a_row_for_another_workspace_is_rejected()
    {
        var (workspaceA, ownerA) = Seed(TestData.NewSlug("wa"));
        var (workspaceB, _) = Seed(TestData.NewSlug("wb"));

        using var scope = fixture.Factory.Services.CreateScope();
        SetAmbient(scope, workspaceA, ownerA);
        var db = scope.ServiceProvider.GetRequiredService<PlanvexaDbContext>();

        // Operating in workspace A, attempt to persist a member that belongs to workspace B.
        var crossWorkspaceMember = WorkspaceMember.Create(
            Guid.CreateVersion7(), workspaceB, Guid.CreateVersion7(), MembershipRole.Member, DateTimeOffset.UtcNow);
        db.Add(crossWorkspaceMember);

        await Should.ThrowAsync<CrossWorkspaceAccessException>(async () => await db.SaveChangesAsync());
    }

    [Fact]
    public async Task Domain_events_are_written_to_the_outbox_in_the_same_transaction()
    {
        var (workspaceA, ownerA) = Seed(TestData.NewSlug("ob"));

        using var scope = fixture.Factory.Services.CreateScope();
        SetAmbient(scope, workspaceA, ownerA);
        var db = scope.ServiceProvider.GetRequiredService<PlanvexaDbContext>();

        var events = await db.OutboxMessages
            .Where(m => m.WorkspaceId == workspaceA)
            .Select(m => m.Type)
            .ToListAsync();

        events.ShouldContain(t => t.Contains("WorkspaceCreated", StringComparison.Ordinal));
    }

    private (Guid WorkspaceId, Guid OwnerId) Seed(string slug)
    {
        // Seeding two workspaces at once is cross-workspace by definition, so it uses the maintenance
        // connection exactly like the platform's background workers do.
        using var scope = fixture.Factory.Services.CreateScope().UseMaintenanceConnection();
        SetAmbient(scope, WorkspaceContext.None);
        var db = scope.ServiceProvider.GetRequiredService<PlanvexaDbContext>();

        var workspaceId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        db.Workspaces.Add(Workspace.Create(workspaceId, slug, slug, ownerId, now));
        // A real Workspace always gets an Owner membership in the same transaction
        // (WorkspaceRegistrationService); tenancy.workspaces is only readable via the user-scoped
        // bootstrap_workspace_read RLS policy, so isolation tests need this to be realistic.
        db.WorkspaceMembers.Add(WorkspaceMember.Create(Guid.CreateVersion7(), workspaceId, ownerId, MembershipRole.Owner, now));
        db.SaveChanges();

        return (workspaceId, ownerId);
    }

    private static void SetAmbient(IServiceScope scope, Guid workspaceId, Guid userId)
    {
        SetAmbient(scope, new WorkspaceContext(
            workspaceId, userId, null, "Owner", new HashSet<string>(), new HashSet<string>(), "corr"));
        scope.ServiceProvider.GetRequiredService<CurrentUser>().Set(userId, "sub", "owner@planvexa.test", "Owner");
    }

    private static void SetAmbient(IServiceScope scope, IWorkspaceContext context)
        => scope.ServiceProvider.GetRequiredService<IWorkspaceContextAccessor>().Set(context);
}
