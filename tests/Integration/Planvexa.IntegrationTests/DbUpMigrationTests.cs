namespace Planvexa.IntegrationTests;

using Npgsql;
using Planvexa.Database;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class DbUpMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("planvexa")
        .WithUsername("planvexa")
        .WithPassword("planvexa")
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task BlankDatabase_UpgradesSuccessfully_AndSecondRunIsNoop()
    {
        var first = PlanvexaDatabase.Upgrade(ConnectionString);
        first.Successful.ShouldBeTrue();
        first.ExecutedScripts.Count.ShouldBe(PlanvexaDatabase.ScriptNames.Length);

        await TableExistsAsync("tenancy", "tenants").ShouldBeFalseAsync();
        await TableExistsAsync("tenancy", "workspaces").ShouldBeTrueAsync();
        await TableExistsAsync("work", "tasks").ShouldBeTrueAsync();
        await TableExistsAsync("chat", "messages").ShouldBeTrueAsync();

        var second = PlanvexaDatabase.Upgrade(ConnectionString);
        second.Successful.ShouldBeTrue();
        second.ExecutedScripts.ShouldBeEmpty();

        var journalCount = await ScalarAsync<long>("SELECT count(*) FROM platform.schema_versions;");
        journalCount.ShouldBe(PlanvexaDatabase.ScriptNames.Length);
    }

    [Fact]
    public async Task EfCreatedDatabase_IsBaselinedWithoutReexecutingScripts()
    {
        await CreateEfLikeBaselineAsync();

        var result = PlanvexaDatabase.Upgrade(ConnectionString);

        result.Successful.ShouldBeTrue();
        result.ExecutedScripts.ShouldBeEmpty();
        var journalCount = await ScalarAsync<long>("SELECT count(*) FROM platform.schema_versions;");
        journalCount.ShouldBe(PlanvexaDatabase.ScriptNames.Length);
    }

    [Fact]
    public async Task UnknownPartialSchema_IsRejected()
    {
        await ExecuteAsync("CREATE SCHEMA IF NOT EXISTS tenancy; CREATE TABLE tenancy.tenants (id uuid PRIMARY KEY);");

        Should.Throw<DatabaseUpgradeException>(() => PlanvexaDatabase.Upgrade(ConnectionString))
            .Message.ShouldContain("unknown or partially migrated schema");
    }

    [Fact]
    public async Task FailedOrInterruptedUpgrade_CanBeRerun()
    {
        await ExecuteAsync("CREATE SCHEMA IF NOT EXISTS platform;");

        var result = PlanvexaDatabase.Upgrade(ConnectionString);

        result.Successful.ShouldBeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM platform.schema_versions;")).ShouldBe(PlanvexaDatabase.ScriptNames.Length);
    }

    [Fact]
    public async Task RlsIndexesConstraintsAndGrantsExistAfterUpgrade()
    {
        PlanvexaDatabase.Upgrade(ConnectionString);
        await CreateAppRoleAsync();

        (await ScalarAsync<bool>("SELECT relrowsecurity FROM pg_class WHERE oid = 'work.tasks'::regclass;")).ShouldBeTrue();
        (await ScalarAsync<bool>("SELECT relforcerowsecurity FROM pg_class WHERE oid = 'work.tasks'::regclass;")).ShouldBeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM pg_policies WHERE schemaname = 'work' AND tablename = 'tasks' AND policyname = 'workspace_isolation';")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM pg_policies WHERE schemaname = 'work' AND tablename = 'tasks' AND policyname = 'tenant_isolation';")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE schemaname = 'work' AND tablename = 'tasks' AND indexname ILIKE '%tenant%';")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE schemaname = 'work' AND tablename = 'tasks' AND indexname ILIKE '%workspace%';")).ShouldBeGreaterThan(0);
        (await ScalarAsync<long>("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'work' AND table_name = 'tasks' AND column_name = 'tenant_id';")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM information_schema.table_constraints WHERE table_schema = 'work' AND table_name = 'tasks' AND constraint_type = 'FOREIGN KEY';")).ShouldBeGreaterThan(0);
        (await ScalarAsync<bool>("SELECT has_table_privilege('planvexa_app', 'chat.messages', 'INSERT');")).ShouldBeTrue();
    }

    [Fact]
    public async Task WorkspaceIdIsEnforcedOnMigratedTenantOnlyTables()
    {
        PlanvexaDatabase.Upgrade(ConnectionString);

        var nullableColumns = await ScalarAsync<long>("""
            SELECT count(*)
            FROM information_schema.columns
            WHERE column_name = 'workspace_id'
              AND is_nullable = 'YES'
              AND (table_schema, table_name) IN (
                ('tenancy', 'feature_entitlements'),
                ('billing', 'subscriptions'),
                ('billing', 'invoices'),
                ('billing', 'invoice_lines'),
                ('billing', 'provider_events'),
                ('ai', 'provider_settings'),
                ('governance', 'retention_policies'),
                ('governance', 'enterprise_security_settings'),
                ('mobile', 'device_registrations'),
                ('integrations', 'personal_access_tokens'),
                ('notifications', 'notification_preferences'),
                ('tenancy', 'workspace_members'),
                ('tenancy', 'invitations'),
                ('tenancy', 'teams'),
                ('tenancy', 'workspaces')
              );
            """);

        nullableColumns.ShouldBe(0L);

        (await ScalarAsync<long>("SELECT count(*) FROM pg_policies WHERE schemaname = 'tenancy' AND tablename = 'tenants' AND policyname = 'bootstrap_tenant_read';")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM pg_policies WHERE schemaname = 'tenancy' AND tablename = 'feature_entitlements' AND policyname = 'bootstrap_entitlement_read';")).ShouldBe(0);
        (await ScalarAsync<bool>("SELECT to_regclass('billing.subscriptions') IS NULL;")).ShouldBeTrue();
        (await ScalarAsync<bool>("SELECT column_name IS NOT NULL FROM information_schema.columns WHERE table_schema = 'tenancy' AND table_name = 'feature_entitlements' AND column_name = 'workspace_id';")).ShouldBeTrue();
    }

    [Fact]
    public async Task TenantLayer_IsFullyRemoved_AndWorkspaceIsTheSoleIsolationBoundary()
    {
        // ADR 0015: finishes the Tenant->Workspace migration. Nothing in the schema should
        // reference "tenant" any more, and workspace_isolation must be the sole PERMISSIVE policy behind
        // RLS wherever it used to sit alongside a tenant_isolation policy.
        PlanvexaDatabase.Upgrade(ConnectionString);

        (await ScalarAsync<bool>("SELECT to_regclass('tenancy.tenants') IS NULL;")).ShouldBeTrue();

        (await ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE column_name = 'tenant_id';"))
            .ShouldBe(0L);

        (await ScalarAsync<long>(
            "SELECT count(*) FROM pg_policies WHERE policyname LIKE '%tenant%';"))
            .ShouldBe(0L);

        await CreateAppRoleAsync();
        (await ScalarAsync<long>(
            "SELECT count(*) FROM pg_policies WHERE schemaname = 'work' AND tablename = 'tasks' AND policyname = 'workspace_isolation' AND permissive = 'PERMISSIVE';"))
            .ShouldBe(1L);
    }

    [Fact]
    public async Task ConcurrentMigrationAttempts_DoNotExecuteScriptsTwice()
    {
        await Task.WhenAll(
            Task.Run(() => PlanvexaDatabase.Upgrade(ConnectionString)),
            Task.Run(() => PlanvexaDatabase.Upgrade(ConnectionString)),
            Task.Run(() => PlanvexaDatabase.Upgrade(ConnectionString)));

        var journalCount = await ScalarAsync<long>("SELECT count(*) FROM platform.schema_versions;");
        journalCount.ShouldBe(PlanvexaDatabase.ScriptNames.Length);
    }

    [Fact]
    public async Task ChildWorkspaceColumns_AreBackfilledAndNotNull()
    {
        // 0023 backfilled workspace_id on child tables from their parent; 0024
        // locked it NOT NULL; 0030 dropped tenant_id entirely (Workspace is the sole boundary now).
        // Prove the parent/child join still resolves, and the column is NOT NULL in the final schema.
        PlanvexaDatabase.Upgrade(ConnectionString);

        var workspaceId = Guid.CreateVersion7();
        var schemeId = Guid.CreateVersion7();
        var statusId = Guid.CreateVersion7();

        await ExecuteAsync($"""
            INSERT INTO work.status_schemes (id, workspace_id, name, is_default)
            VALUES ('{schemeId}', '{workspaceId}', 'Default', true);
            INSERT INTO work.statuses (id, scheme_id, name, category, color, position, workspace_id)
            VALUES ('{statusId}', '{schemeId}', 'Open', 'todo', '#ffffff', 1, '{workspaceId}');
            """);

        var backfilled = await ScalarAsync<Guid>(
            $"SELECT p.workspace_id FROM work.statuses s JOIN work.status_schemes p ON s.scheme_id = p.id WHERE s.id = '{statusId}';");
        backfilled.ShouldBe(workspaceId);

        var isNullable = await ScalarAsync<string>(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'work' AND table_name = 'statuses' AND column_name = 'workspace_id';");
        isNullable.ShouldBe("NO");

        var nullableChildColumns = await ScalarAsync<long>("""
            SELECT count(*) FROM information_schema.columns
            WHERE column_name = 'workspace_id' AND is_nullable = 'YES'
              AND (table_schema, table_name) IN (
                ('work','task_checklists'), ('work','task_dependencies'), ('work','task_checklist_items'),
                ('work','custom_field_options'), ('work','custom_field_values'), ('work','recurring_occurrences'),
                ('work','statuses'), ('work','task_assignees'), ('work','task_tags'), ('work','task_watchers'),
                ('collab','comment_reactions'), ('collab','mentions'), ('notifications','notification_deliveries'),
                ('time','time_entry_audits'), ('time','timesheet_approvals'), ('reporting','dashboard_widgets'),
                ('planning','sprint_items'), ('docs','document_versions'), ('forms','form_fields'),
                ('chat','channel_members'));
            """);
        nullableChildColumns.ShouldBe(0L);
    }

    [Fact]
    public async Task RolesAndRolePermissions_HaveWorkspaceScopedRlsAndTheMemberRoleIdForeignKey()
    {
        // ADR-0003: 0031 creates tenancy.roles/tenancy.role_permissions with the same
        // sole-PERMISSIVE workspace_isolation policy as every other workspace-owned table; 0033 adds
        // the nullable tenancy.workspace_members.role_id FK. Proven on a blank database (AGENTS.md
        // rule 9's "empty DB" half — the "upgraded DB" half is proven by the backfill test below).
        PlanvexaDatabase.Upgrade(ConnectionString);
        await CreateAppRoleAsync();

        (await ScalarAsync<bool>("SELECT relrowsecurity FROM pg_class WHERE oid = 'tenancy.roles'::regclass;")).ShouldBeTrue();
        (await ScalarAsync<bool>("SELECT relforcerowsecurity FROM pg_class WHERE oid = 'tenancy.roles'::regclass;")).ShouldBeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM pg_policies WHERE schemaname = 'tenancy' AND tablename = 'roles' AND policyname = 'workspace_isolation' AND permissive = 'PERMISSIVE';")).ShouldBe(1);

        (await ScalarAsync<bool>("SELECT relrowsecurity FROM pg_class WHERE oid = 'tenancy.role_permissions'::regclass;")).ShouldBeTrue();
        (await ScalarAsync<bool>("SELECT relforcerowsecurity FROM pg_class WHERE oid = 'tenancy.role_permissions'::regclass;")).ShouldBeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM pg_policies WHERE schemaname = 'tenancy' AND tablename = 'role_permissions' AND policyname = 'workspace_isolation' AND permissive = 'PERMISSIVE';")).ShouldBe(1);

        (await ScalarAsync<long>("SELECT count(*) FROM information_schema.table_constraints WHERE table_schema = 'tenancy' AND table_name = 'role_permissions' AND constraint_type = 'PRIMARY KEY';")).ShouldBe(1);

        var roleIdNullable = await ScalarAsync<string>(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'tenancy' AND table_name = 'workspace_members' AND column_name = 'role_id';");
        roleIdNullable.ShouldBe("YES");

        (await ScalarAsync<bool>("SELECT has_table_privilege('planvexa_app', 'tenancy.roles', 'SELECT');")).ShouldBeTrue();
        (await ScalarAsync<bool>("SELECT has_table_privilege('planvexa_app', 'tenancy.role_permissions', 'INSERT');")).ShouldBeTrue();
    }

    [Fact]
    public async Task BuiltInRoleBackfill_SeedsExistingWorkspacesAndLinksExistingMembers()
    {
        // Proves AGENTS.md rule 9's "upgraded DB" half for 0032/0033: a workspace and member that
        // existed BEFORE those scripts ran (simulated here by inserting directly, bypassing the app
        // layer — exactly what a workspace created under the original schema looked like) still end
        // up with the five built-in roles and a correctly-linked role_id after the backfill runs.
        PlanvexaDatabase.Upgrade(ConnectionString);

        var workspaceId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var membershipId = Guid.CreateVersion7();

        await ExecuteAsync($"""
            INSERT INTO identity.users (id, subject, email, display_name, is_active, created_at_utc)
            VALUES ('{userId}', 'legacy-{userId:N}', 'legacy-{userId:N}@planvexa.test', 'Legacy Owner', true, now());
            INSERT INTO tenancy.workspaces (id, workspace_id, name, slug, status, created_by_user_id, created_at_utc)
            VALUES ('{workspaceId}', '{workspaceId}', 'Legacy Workspace', 'legacy-{workspaceId:N}', 'Active', '{userId}', now());
            INSERT INTO tenancy.workspace_members (id, workspace_id, user_id, role, is_guest, status, joined_at_utc)
            VALUES ('{membershipId}', '{workspaceId}', '{userId}', 'Owner', false, 'Active', now());
            """);

        // 0032/0033 already ran (against zero workspaces) as part of the Upgrade() above and won't run
        // again — DbUp journals each script once. Re-execute their exact shipped SQL text directly to
        // prove it is safe and correct against this pre-existing data (the scripts are idempotent
        // NOT EXISTS-guarded backfills, so running them a second time here is exactly what "safe on an
        // upgraded DB" means).
        await ExecuteAsync(ReadScript("0032_SeedBuiltInRolesForExistingWorkspaces"));
        await ExecuteAsync(ReadScript("0033_AddWorkspaceMemberRoleId"));

        var roleCount = await ScalarAsync<long>($"SELECT count(*) FROM tenancy.roles WHERE workspace_id = '{workspaceId}';");
        roleCount.ShouldBe(5L);

        var ownerPermissionCount = await ScalarAsync<long>($"""
            SELECT count(*) FROM tenancy.role_permissions rp
            JOIN tenancy.roles r ON r.id = rp.role_id
            WHERE r.workspace_id = '{workspaceId}' AND r.key = 'owner';
            """);
        ownerPermissionCount.ShouldBe(14L);

        var linkedRoleKey = await ScalarAsync<string>($"""
            SELECT r.key FROM tenancy.workspace_members m
            JOIN tenancy.roles r ON r.id = m.role_id
            WHERE m.id = '{membershipId}';
            """);
        linkedRoleKey.ShouldBe("owner");
    }

    [Fact]
    public async Task DuplicateUserRows_AreMergedAndUniqueIndexesAreEnforced()
    {
        // Proves AGENTS.md rule 9's "upgraded DB" half for 0091: two identity.users rows sharing an
        // email (the TOCTOU race UserDirectory.GetOrProvisionAsync could lose before this script — see
        // its header comment) get merged into one, with every reference repointed and any resulting
        // duplicate (two workspace_members rows for the same workspace) resolved rather than left to
        // violate the newly-added unique index.
        PlanvexaDatabase.Upgrade(ConnectionString);

        // Simulate the pre-0091 world: these indexes didn't exist yet, which is exactly how duplicates
        // could accumulate in the first place.
        await ExecuteAsync("DROP INDEX identity.ux_users_email; DROP INDEX identity.ux_users_subject;");

        var keeperId = Guid.CreateVersion7();
        var loserId = Guid.CreateVersion7();
        var workspaceId = Guid.CreateVersion7();
        var keeperMembershipId = Guid.CreateVersion7();
        var loserMembershipId = Guid.CreateVersion7();
        const string email = "dup-race@planvexa.test";

        await ExecuteAsync($"""
            INSERT INTO identity.users (id, subject, email, display_name, is_active, created_at_utc)
            VALUES ('{keeperId}', 'dup-race-keeper', '{email}', 'Keeper', true, now() - interval '1 day');
            INSERT INTO identity.users (id, subject, email, display_name, is_active, created_at_utc)
            VALUES ('{loserId}', 'dup-race-loser', '{email}', 'Loser', true, now());

            INSERT INTO tenancy.workspaces (id, workspace_id, name, slug, status, created_by_user_id, created_at_utc)
            VALUES ('{workspaceId}', '{workspaceId}', 'Dup Race Workspace', 'dup-race-{workspaceId:N}', 'Active', '{loserId}', now());

            -- The collision case: both the keeper and the loser somehow ended up as separate members of
            -- the same workspace (exactly what the race could produce — two "different" identities each
            -- completing onboarding/invite-accept once).
            INSERT INTO tenancy.workspace_members (id, workspace_id, user_id, role, is_guest, status, joined_at_utc)
            VALUES ('{keeperMembershipId}', '{workspaceId}', '{keeperId}', 'Owner', false, 'Active', now() - interval '1 day');
            INSERT INTO tenancy.workspace_members (id, workspace_id, user_id, role, is_guest, status, joined_at_utc)
            VALUES ('{loserMembershipId}', '{workspaceId}', '{loserId}', 'Member', false, 'Active', now());
            """);

        await ExecuteAsync(ReadScript("0091_DedupeUsersAndEnforceUniqueIdentity"));

        (await ScalarAsync<long>($"SELECT count(*) FROM identity.users WHERE lower(email) = '{email}';")).ShouldBe(1L);
        (await ScalarAsync<Guid>($"SELECT id FROM identity.users WHERE lower(email) = '{email}';")).ShouldBe(keeperId);

        // Plain reference: the workspace's creator is repointed from loser to keeper.
        (await ScalarAsync<Guid>($"SELECT created_by_user_id FROM tenancy.workspaces WHERE id = '{workspaceId}';")).ShouldBe(keeperId);

        // Collision case: the keeper already had a membership in this workspace, so the loser's
        // redundant one was dropped rather than repointed (which would have violated the unique index)
        // — exactly one membership remains for (workspace, keeper), and it is the keeper's original
        // Owner row, not a clobbered copy of the loser's Member row.
        (await ScalarAsync<long>($"SELECT count(*) FROM tenancy.workspace_members WHERE workspace_id = '{workspaceId}' AND user_id = '{keeperId}';")).ShouldBe(1L);
        (await ScalarAsync<string>($"SELECT role FROM tenancy.workspace_members WHERE id = '{keeperMembershipId}';")).ShouldBe("Owner");
        (await ScalarAsync<long>($"SELECT count(*) FROM tenancy.workspace_members WHERE id = '{loserMembershipId}';")).ShouldBe(0L);

        // The unique indexes are back in place.
        (await ScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE schemaname = 'identity' AND indexname = 'ux_users_email';")).ShouldBe(1L);
        (await ScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE schemaname = 'identity' AND indexname = 'ux_users_subject';")).ShouldBe(1L);
    }

    private static string ReadScript(string nameContains)
    {
        var assembly = typeof(PlanvexaDatabase).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.Contains(".Scripts.", StringComparison.Ordinal) && n.Contains(nameContains, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task CreateEfLikeBaselineAsync()
    {
        PlanvexaDatabase.Upgrade(ConnectionString);
        await ExecuteAsync("DELETE FROM platform.schema_versions;");
        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform.__ef_migrations_history (
                migration_id character varying(150) NOT NULL PRIMARY KEY,
                product_version character varying(32) NOT NULL
            );
            """);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var migrationId in PlanvexaDatabase.EfMigrationIds)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO platform.__ef_migrations_history (migration_id, product_version) VALUES (@migration_id, '10.0.10') ON CONFLICT DO NOTHING;";
            command.Parameters.AddWithValue("migration_id", migrationId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task CreateAppRoleAsync()
    {
        await ExecuteAsync("DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'planvexa_app') THEN CREATE ROLE planvexa_app LOGIN PASSWORD 'app' NOSUPERUSER NOBYPASSRLS; END IF; END $$;");
        await ExecuteAsync("GRANT USAGE ON SCHEMA tenancy, audit, identity, platform, work, collab, sharing, notifications, time, planning, reporting, docs, forms, automation, integrations, governance, ai, mobile, chat, whiteboards, clips TO planvexa_app;");
        await ExecuteAsync("GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tenancy, audit, identity, platform, work, collab, sharing, notifications, time, planning, reporting, docs, forms, automation, integrations, governance, ai, mobile, chat, whiteboards, clips TO planvexa_app;");
    }

    private async Task<bool> TableExistsAsync(string schema, string table) =>
        await ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table);",
            command =>
            {
                command.Parameters.AddWithValue("schema", schema);
                command.Parameters.AddWithValue("table", table);
            });

    private async Task<T> ScalarAsync<T>(string sql, Action<NpgsqlCommand>? configure = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure?.Invoke(command);
        var result = await command.ExecuteScalarAsync();
        return (T)(result ?? throw new InvalidOperationException("Expected scalar result."));
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

internal static class ShouldlyTaskExtensions
{
    public static async Task ShouldBeTrueAsync(this Task<bool> task) => (await task).ShouldBeTrue();

    public static async Task ShouldBeFalseAsync(this Task<bool> task) => (await task).ShouldBeFalse();
}
