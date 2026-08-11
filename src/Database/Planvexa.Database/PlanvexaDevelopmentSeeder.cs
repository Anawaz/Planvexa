namespace Planvexa.Database;

using Npgsql;

public static class PlanvexaDevelopmentSeeder
{
    public const string OwnerSubject = "dev-owner";
    public const string AdminSubject = "dev-admin";
    public const string MemberSubject = "dev-member";
    public const string GuestSubject = "dev-guest";

    private const string WorkspaceOpsId = "018f0000-0000-7000-8000-000000000101";
    private const string WorkspaceClientId = "018f0000-0000-7000-8000-000000000102";
    private const string OwnerId = "018f0000-0000-7000-8000-000000001001";
    private const string AdminId = "018f0000-0000-7000-8000-000000001002";
    private const string MemberId = "018f0000-0000-7000-8000-000000001003";
    private const string GuestId = "018f0000-0000-7000-8000-000000001004";

    public static async Task SeedAsync(string connectionString, bool seedDevelopmentData, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        log ??= _ => { };

        if (!seedDevelopmentData)
        {
            log("Development data seeding disabled (Database:SeedDevelopmentData=false).");
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteAsync(connection, transaction, Sql, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            log("Development seed applied: 2 workspaces, work/collab/chat/time/planning/docs/forms/integrations/governance/mobile/AI sample data.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }


    public static async Task ResetAsync(string connectionString, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        log ??= _ => { };
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteAsync(connection, transaction, ResetSql, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            log("Development seed reset completed.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    private static readonly string ResetSql = $$"""
SELECT set_config('app.current_workspace', '{{WorkspaceOpsId}}', false);
DELETE FROM governance.export_jobs WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM governance.retention_policies WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM governance.enterprise_security_settings WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM integrations.webhook_deliveries WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM integrations.webhook_subscriptions WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM integrations.personal_access_tokens WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM automation.automation_runs WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM automation.automation_rules WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM forms.form_submissions WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM forms.form_fields WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM forms.forms WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM docs.document_versions WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM docs.documents WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM reporting.dashboard_widgets WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM reporting.dashboards WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM planning.sprint_items WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM planning.sprints WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM planning.task_estimates WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM planning.leave_entries WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM planning.holidays WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM planning.work_schedules WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM time.timesheet_approvals WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM time.timesheet_periods WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM time.time_entry_audits WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM time.time_entries WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM time.member_rates WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM time.time_policies WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM chat.messages WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM chat.channel_members WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM chat.channels WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM notifications.notification_deliveries WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM notifications.notification_preferences WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM notifications.notifications WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM collab.comment_reactions WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM collab.mentions WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM collab.comments WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM sharing.share_links WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.custom_field_values WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.custom_field_options WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.custom_field_definitions WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.saved_views WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.task_tags WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.tags WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.task_dependencies WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.task_checklist_items WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.task_checklists WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.task_assignees WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.task_watchers WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.tasks WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.lists WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.folders WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.spaces WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.statuses WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM work.status_schemes WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM tenancy.feature_entitlements WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM tenancy.invitations WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM tenancy.workspace_members WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM tenancy.workspaces WHERE id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM audit.audit_events WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM platform.outbox_messages WHERE workspace_id IN ('{{WorkspaceOpsId}}', '{{WorkspaceClientId}}');
DELETE FROM identity.users WHERE subject IN ('{{OwnerSubject}}', '{{AdminSubject}}', '{{MemberSubject}}', '{{GuestSubject}}');
""";
    private static readonly string Sql = $$"""
INSERT INTO identity.users (id, subject, email, display_name, is_active, created_at_utc)
VALUES
('{{OwnerId}}', '{{OwnerSubject}}', 'owner@planvexa.local', 'Dev Owner', true, '2026-07-01T00:00:00Z'),
('{{AdminId}}', '{{AdminSubject}}', 'admin@planvexa.local', 'Dev Admin', true, '2026-07-01T00:00:00Z'),
('{{MemberId}}', '{{MemberSubject}}', 'member@planvexa.local', 'Dev Member', true, '2026-07-01T00:00:00Z'),
('{{GuestId}}', '{{GuestSubject}}', 'guest@planvexa.local', 'Dev Guest', true, '2026-07-01T00:00:00Z')
ON CONFLICT (id) DO UPDATE SET email = EXCLUDED.email, display_name = EXCLUDED.display_name, is_active = true;

SELECT set_config('app.current_workspace', '{{WorkspaceOpsId}}', false);

INSERT INTO tenancy.workspaces (id, workspace_id, name, slug, status, created_by_user_id, created_at_utc)
VALUES
('{{WorkspaceOpsId}}', '{{WorkspaceOpsId}}', 'Product Operations', 'product-operations', 'Active', '{{OwnerId}}', '2026-07-01T00:00:00Z'),
('{{WorkspaceClientId}}', '{{WorkspaceClientId}}', 'Client Portal', 'client-portal', 'Active', '{{OwnerId}}', '2026-07-01T00:00:00Z')
ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, slug = EXCLUDED.slug, status = EXCLUDED.status;

INSERT INTO tenancy.workspace_members (id, workspace_id, user_id, role, is_guest, status, joined_at_utc)
VALUES
('018f0000-0000-7000-8000-000000002001', '{{WorkspaceOpsId}}', '{{OwnerId}}', 'Owner', false, 'Active', '2026-07-01T00:00:00Z'),
('018f0000-0000-7000-8000-000000002002', '{{WorkspaceOpsId}}', '{{AdminId}}', 'Admin', false, 'Active', '2026-07-01T00:00:00Z'),
('018f0000-0000-7000-8000-000000002003', '{{WorkspaceOpsId}}', '{{MemberId}}', 'Member', false, 'Active', '2026-07-01T00:00:00Z'),
('018f0000-0000-7000-8000-000000002004', '{{WorkspaceClientId}}', '{{GuestId}}', 'Guest', true, 'Active', '2026-07-01T00:00:00Z')
ON CONFLICT (id) DO UPDATE SET role = EXCLUDED.role, is_guest = EXCLUDED.is_guest, status = EXCLUDED.status;

INSERT INTO work.status_schemes (id, workspace_id, name, is_default)
VALUES ('018f0000-0000-7000-8000-000000010001', '{{WorkspaceOpsId}}', 'Default delivery workflow', true)
ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, is_default = true;

INSERT INTO work.statuses (id, workspace_id, scheme_id, name, category, color, position)
VALUES
('018f0000-0000-7000-8000-000000010101', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000010001', 'Backlog', 'NotStarted', '#64748b', 1),
('018f0000-0000-7000-8000-000000010102', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000010001', 'To Do', 'NotStarted', '#2563eb', 2),
('018f0000-0000-7000-8000-000000010103', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000010001', 'In Progress', 'Active', '#f59e0b', 3),
('018f0000-0000-7000-8000-000000010104', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000010001', 'Review', 'Active', '#8b5cf6', 4),
('018f0000-0000-7000-8000-000000010105', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000010001', 'Blocked', 'Active', '#dc2626', 5),
('018f0000-0000-7000-8000-000000010106', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000010001', 'Complete', 'Done', '#16a34a', 6)
ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, category = EXCLUDED.category, color = EXCLUDED.color, position = EXCLUDED.position;

INSERT INTO work.spaces (id, name, description, color, icon, workspace_id, position, is_archived, is_deleted, created_at_utc, created_by_user_id)
VALUES
('018f0000-0000-7000-8000-000000011001', 'Product & Engineering', 'Build and ship the core Planvexa product.', '#2563eb', 'PE', '{{WorkspaceOpsId}}', 1, false, false, '2026-07-01T00:00:00Z', '{{OwnerId}}'),
('018f0000-0000-7000-8000-000000011002', 'Go-to-Market', 'Launch, content, and customer-facing work.', '#7c3aed', 'GT', '{{WorkspaceOpsId}}', 2, false, false, '2026-07-01T00:00:00Z', '{{OwnerId}}'),
-- Automated tests write here instead of into the demo lists; see apps/web/e2e/helpers/fixtures.ts.
('018f0000-0000-7000-8000-000000011901', 'E2E Sandbox', 'Scratch space for automated end-to-end tests. Safe to empty.', '#64748b', 'QA', '{{WorkspaceOpsId}}', 3, false, false, '2026-07-01T00:00:00Z', '{{OwnerId}}')
ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, description = EXCLUDED.description, color = EXCLUDED.color, icon = EXCLUDED.icon;

INSERT INTO work.lists (id, space_id, folder_id, name, description, status_scheme_id, task_counter, workspace_id, position, is_archived, is_deleted, created_at_utc, created_by_user_id)
VALUES
('018f0000-0000-7000-8000-000000012001', '018f0000-0000-7000-8000-000000011001', NULL, 'Current Sprint', 'Delivery work for the active sprint.', '018f0000-0000-7000-8000-000000010001', 100, '{{WorkspaceOpsId}}', 1, false, false, '2026-07-01T00:00:00Z', '{{OwnerId}}'),
('018f0000-0000-7000-8000-000000012002', '018f0000-0000-7000-8000-000000011002', NULL, 'Launch Plan', 'Public launch and customer readiness.', '018f0000-0000-7000-8000-000000010001', 50, '{{WorkspaceOpsId}}', 2, false, false, '2026-07-01T00:00:00Z', '{{OwnerId}}'),
-- Seeded empty on purpose: the E2E write specs create and delete their tasks here.
('018f0000-0000-7000-8000-000000012901', '018f0000-0000-7000-8000-000000011901', NULL, 'E2E Sandbox', 'Scratch list for automated end-to-end tests. Safe to empty.', '018f0000-0000-7000-8000-000000010001', 0, '{{WorkspaceOpsId}}', 1, false, false, '2026-07-01T00:00:00Z', '{{OwnerId}}')
ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, description = EXCLUDED.description;

INSERT INTO work.tasks (id, space_id, list_id, parent_id, sequence, title, description, status_id, priority, start_date, due_date, is_milestone, is_completed, completed_at_utc, completed_by_user_id, workspace_id, position, is_archived, is_deleted, created_at_utc, created_by_user_id)
VALUES
('018f0000-0000-7000-8000-000000013001', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', NULL, 1, 'Wire real API clients', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Replace mock clients with authenticated API calls.'))))), '018f0000-0000-7000-8000-000000010103', 'High', '2026-07-30T00:00:00Z', '2026-08-05T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 1, false, false, '2026-07-01T00:00:00Z', '{{OwnerId}}'),
('018f0000-0000-7000-8000-000000013002', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', '018f0000-0000-7000-8000-000000013001', 2, 'Implement tenant context provider', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Load accessible tenants and workspaces from the API.'))))), '018f0000-0000-7000-8000-000000010102', 'Normal', '2026-07-31T00:00:00Z', '2026-08-04T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 2, false, false, '2026-07-01T00:00:00Z', '{{AdminId}}'),
('018f0000-0000-7000-8000-000000013003', '018f0000-0000-7000-8000-000000011002', '018f0000-0000-7000-8000-000000012002', NULL, 1, 'Publish beta launch checklist', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Coordinate documents, forms, webhooks, and reports.'))))), '018f0000-0000-7000-8000-000000010106', 'High', '2026-07-20T00:00:00Z', '2026-07-29T00:00:00Z', true, true, '2026-07-29T16:00:00Z', '{{MemberId}}', '{{WorkspaceOpsId}}', 3, false, false, '2026-07-01T00:00:00Z', '{{MemberId}}'),
-- Demo filler from here down: enough work across every status, priority and due-date bucket that a
-- first login looks like a live workspace. Sequences stay below each list's task_counter (100/50),
-- so tasks created through the API can never collide with a seeded one.
('018f0000-0000-7000-8000-000000013004', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', '018f0000-0000-7000-8000-000000013001', 3, 'Surface API errors in the task panel', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Inline alert plus retry for failed mutations.'))))), '018f0000-0000-7000-8000-000000010104', 'Normal', '2026-07-31T00:00:00Z', '2026-08-04T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 4, false, false, '2026-07-02T00:00:00Z', '{{AdminId}}'),
('018f0000-0000-7000-8000-000000013005', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', NULL, 4, 'Ship board drag-and-drop', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Pointer and keyboard reordering with server-side persistence.'))))), '018f0000-0000-7000-8000-000000010103', 'Urgent', '2026-07-28T00:00:00Z', '2026-08-01T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 5, false, false, '2026-07-02T00:00:00Z', '{{OwnerId}}'),
('018f0000-0000-7000-8000-000000013006', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', '018f0000-0000-7000-8000-000000013005', 5, 'Tune the pointer sensor threshold', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Eight pixels before a drag starts, so taps still open the task.'))))), '018f0000-0000-7000-8000-000000010106', 'Normal', '2026-07-28T00:00:00Z', '2026-07-30T00:00:00Z', false, true, '2026-07-30T15:20:00Z', '{{OwnerId}}', '{{WorkspaceOpsId}}', 6, false, false, '2026-07-02T00:00:00Z', '{{OwnerId}}'),
('018f0000-0000-7000-8000-000000013007', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', '018f0000-0000-7000-8000-000000013005', 6, 'Keyboard status select on every card', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Column moves without a mouse.'))))), '018f0000-0000-7000-8000-000000010104', 'Low', '2026-07-29T00:00:00Z', '2026-08-03T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 7, false, false, '2026-07-02T00:00:00Z', '{{AdminId}}'),
('018f0000-0000-7000-8000-000000013008', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', '018f0000-0000-7000-8000-000000013005', 7, 'Persist column order per list', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Remember the board layout between sessions.'))))), '018f0000-0000-7000-8000-000000010102', 'Normal', '2026-08-03T00:00:00Z', '2026-08-07T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 8, false, false, '2026-07-02T00:00:00Z', '{{MemberId}}'),
('018f0000-0000-7000-8000-000000013009', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', NULL, 8, 'Fix the timesheet week rollover', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Entries land in the previous week for negative UTC offsets.'))))), '018f0000-0000-7000-8000-000000010105', 'Urgent', '2026-07-24T00:00:00Z', '2026-07-28T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 9, false, false, '2026-07-02T00:00:00Z', '{{AdminId}}'),
('018f0000-0000-7000-8000-000000013010', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', NULL, 9, 'Backfill audit events for task moves', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','No owner yet — grooming candidate.'))))), '018f0000-0000-7000-8000-000000010101', 'Low', NULL, NULL, false, false, NULL, NULL, '{{WorkspaceOpsId}}', 10, false, false, '2026-07-02T00:00:00Z', '{{OwnerId}}'),
('018f0000-0000-7000-8000-000000013011', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', NULL, 10, 'Tune the realtime reconnect backoff', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Reconnect storms after a deploy hammer the hub.'))))), '018f0000-0000-7000-8000-000000010102', 'Normal', '2026-07-31T00:00:00Z', '2026-08-01T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 11, false, false, '2026-07-02T00:00:00Z', '{{OwnerId}}'),
('018f0000-0000-7000-8000-000000013012', '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', NULL, 11, 'Adopt the shared status colour tokens', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','One palette for list, board and reports.'))))), '018f0000-0000-7000-8000-000000010106', 'Normal', '2026-07-27T00:00:00Z', '2026-07-31T00:00:00Z', false, true, '2026-07-31T11:45:00Z', '{{OwnerId}}', '{{WorkspaceOpsId}}', 12, false, false, '2026-07-02T00:00:00Z', '{{MemberId}}'),
('018f0000-0000-7000-8000-000000013013', '018f0000-0000-7000-8000-000000011002', '018f0000-0000-7000-8000-000000012002', NULL, 2, 'Write the launch announcement', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Blog post, demo video and support briefing.'))))), '018f0000-0000-7000-8000-000000010103', 'High', '2026-07-29T00:00:00Z', '2026-08-03T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 4, false, false, '2026-07-02T00:00:00Z', '{{AdminId}}'),
('018f0000-0000-7000-8000-000000013014', '018f0000-0000-7000-8000-000000011002', '018f0000-0000-7000-8000-000000012002', '018f0000-0000-7000-8000-000000013013', 3, 'Draft the blog post', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Positioning, screenshots, pricing callout.'))))), '018f0000-0000-7000-8000-000000010104', 'Normal', '2026-07-29T00:00:00Z', '2026-08-01T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 5, false, false, '2026-07-02T00:00:00Z', '{{AdminId}}'),
('018f0000-0000-7000-8000-000000013015', '018f0000-0000-7000-8000-000000011002', '018f0000-0000-7000-8000-000000012002', '018f0000-0000-7000-8000-000000013013', 4, 'Record the 90-second product demo', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','One take of the board, timer and My Work.'))))), '018f0000-0000-7000-8000-000000010102', 'Normal', '2026-08-03T00:00:00Z', '2026-08-06T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 6, false, false, '2026-07-02T00:00:00Z', '{{OwnerId}}'),
('018f0000-0000-7000-8000-000000013016', '018f0000-0000-7000-8000-000000011002', '018f0000-0000-7000-8000-000000012002', NULL, 5, 'Pricing page copy review', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Blocked on the final seat price.'))))), '018f0000-0000-7000-8000-000000010105', 'High', '2026-07-22T00:00:00Z', '2026-07-27T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 7, false, false, '2026-07-02T00:00:00Z', '{{OwnerId}}'),
('018f0000-0000-7000-8000-000000013017', '018f0000-0000-7000-8000-000000011002', '018f0000-0000-7000-8000-000000012002', NULL, 6, 'Stand up the launch-day status page', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Public uptime page before the announcement.'))))), '018f0000-0000-7000-8000-000000010101', 'Low', '2026-08-10T00:00:00Z', '2026-08-20T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 8, false, false, '2026-07-02T00:00:00Z', '{{MemberId}}'),
('018f0000-0000-7000-8000-000000013018', '018f0000-0000-7000-8000-000000011002', '018f0000-0000-7000-8000-000000012002', NULL, 7, 'Brief support on the new workflow', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text','Macros, escalation path and the new statuses.'))))), '018f0000-0000-7000-8000-000000010102', 'Normal', '2026-07-30T00:00:00Z', '2026-08-01T00:00:00Z', false, false, NULL, NULL, '{{WorkspaceOpsId}}', 9, false, false, '2026-07-02T00:00:00Z', '{{AdminId}}')
ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, description = EXCLUDED.description, status_id = EXCLUDED.status_id, priority = EXCLUDED.priority, start_date = EXCLUDED.start_date, due_date = EXCLUDED.due_date, is_completed = EXCLUDED.is_completed, completed_at_utc = EXCLUDED.completed_at_utc, completed_by_user_id = EXCLUDED.completed_by_user_id;

-- Every seeded task needs its primary TaskListMembership row too (WorkItem.ListId is now only the
-- denormalized primary-list pointer; list views read work.task_list_memberships).
INSERT INTO work.task_list_memberships (id, workspace_id, task_id, list_id, is_primary, position, added_at_utc)
SELECT gen_random_uuid(), t.workspace_id, t.id, t.list_id, true, t.position, t.created_at_utc
FROM work.tasks t
WHERE t.workspace_id = '{{WorkspaceOpsId}}'
ON CONFLICT (task_id, list_id) DO NOTHING;

INSERT INTO work.tags (id, workspace_id, name, color) VALUES ('018f0000-0000-7000-8000-000000014001', '{{WorkspaceOpsId}}', 'Launch', '#f97316'), ('018f0000-0000-7000-8000-000000014002', '{{WorkspaceOpsId}}', 'Bug', '#ef4444'), ('018f0000-0000-7000-8000-000000014003', '{{WorkspaceOpsId}}', 'Design', '#0ea5e9') ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, color = EXCLUDED.color;
INSERT INTO work.task_tags (id, workspace_id, task_id, tag_id) VALUES ('018f0000-0000-7000-8000-000000014101', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013003', '018f0000-0000-7000-8000-000000014001'), ('018f0000-0000-7000-8000-000000014102', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013009', '018f0000-0000-7000-8000-000000014002'), ('018f0000-0000-7000-8000-000000014103', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013013', '018f0000-0000-7000-8000-000000014001'), ('018f0000-0000-7000-8000-000000014104', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013012', '018f0000-0000-7000-8000-000000014003') ON CONFLICT (id) DO NOTHING;
INSERT INTO work.task_assignees (id, workspace_id, task_id, user_id, assigned_at_utc) VALUES
('018f0000-0000-7000-8000-000000014201', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013001', '{{MemberId}}', '2026-07-01T00:00:00Z'),
('018f0000-0000-7000-8000-000000014202', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013002', '{{AdminId}}', '2026-07-01T00:00:00Z'),
('018f0000-0000-7000-8000-000000014203', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013003', '{{MemberId}}', '2026-07-01T00:00:00Z'),
('018f0000-0000-7000-8000-000000014204', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013004', '{{MemberId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014205', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013005', '{{OwnerId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014206', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013006', '{{OwnerId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014207', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013007', '{{AdminId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014208', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013008', '{{MemberId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014209', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013009', '{{AdminId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014210', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013011', '{{OwnerId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014211', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013012', '{{OwnerId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014212', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013013', '{{AdminId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014213', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013014', '{{AdminId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014214', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013015', '{{OwnerId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014215', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013016', '{{OwnerId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014216', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013017', '{{MemberId}}', '2026-07-02T00:00:00Z'),
('018f0000-0000-7000-8000-000000014217', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013018', '{{AdminId}}', '2026-07-02T00:00:00Z')
ON CONFLICT (id) DO NOTHING;
INSERT INTO work.task_watchers (id, workspace_id, task_id, user_id, added_at_utc) VALUES ('018f0000-0000-7000-8000-000000014301', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013001', '{{AdminId}}', '2026-07-01T00:00:00Z') ON CONFLICT (id) DO NOTHING;
INSERT INTO work.task_dependencies (id, workspace_id, task_id, depends_on_task_id, type) VALUES ('018f0000-0000-7000-8000-000000014401', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013001', '018f0000-0000-7000-8000-000000013002', 'Blocks') ON CONFLICT (id) DO NOTHING;
INSERT INTO work.task_checklists (id, workspace_id, task_id, name, position) VALUES ('018f0000-0000-7000-8000-000000014501', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013001', 'Integration checklist', 1) ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;
INSERT INTO work.task_checklist_items (id, workspace_id, checklist_id, content, is_resolved, position) VALUES ('018f0000-0000-7000-8000-000000014601', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000014501', 'Replace mock data', false, 1), ('018f0000-0000-7000-8000-000000014602', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000014501', 'Verify tenant isolation', true, 2) ON CONFLICT (id) DO UPDATE SET is_resolved = EXCLUDED.is_resolved;

INSERT INTO work.custom_field_definitions (id, workspace_id, scope, scope_id, name, type, is_required, position) VALUES ('018f0000-0000-7000-8000-000000015001', '{{WorkspaceOpsId}}', 'Workspace', NULL, 'Customer impact', 'Dropdown', false, 1) ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;
INSERT INTO work.custom_field_options (id, workspace_id, definition_id, label, color, position) VALUES ('018f0000-0000-7000-8000-000000015101', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000015001', 'Internal', '#64748b', 1), ('018f0000-0000-7000-8000-000000015102', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000015001', 'Customer-facing', '#ef4444', 2) ON CONFLICT (id) DO UPDATE SET label = EXCLUDED.label;
INSERT INTO work.custom_field_values (id, workspace_id, task_id, definition_id, option_id, updated_at_utc) VALUES ('018f0000-0000-7000-8000-000000015201', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013001', '018f0000-0000-7000-8000-000000015001', '018f0000-0000-7000-8000-000000015102', '2026-07-01T00:00:00Z') ON CONFLICT (id) DO UPDATE SET option_id = EXCLUDED.option_id, updated_at_utc = EXCLUDED.updated_at_utc;
INSERT INTO work.saved_views (id, workspace_id, scope_type, scope_id, name, view_type, config_json, is_private, owner_user_id, created_at_utc) VALUES ('018f0000-0000-7000-8000-000000015301', '{{WorkspaceOpsId}}', 'Workspace', NULL, 'Launch board', 'Board', '{"groupBy":"status"}', false, '{{OwnerId}}', '2026-07-01T00:00:00Z') ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, config_json = EXCLUDED.config_json;

INSERT INTO collab.comments (id, workspace_id, task_id, parent_id, author_user_id, body, is_edited, created_at_utc, is_deleted)
VALUES
('018f0000-0000-7000-8000-000000020001', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013001', NULL, '{{AdminId}}', 'Please verify the BFF path before replacing all mock clients.', false, '2026-07-30T10:00:00Z', false),
('018f0000-0000-7000-8000-000000020002', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013001', '018f0000-0000-7000-8000-000000020001', '{{MemberId}}', 'Acknowledged. I will start with tenant context and work tasks.', false, '2026-07-30T10:05:00Z', false)
ON CONFLICT (id) DO UPDATE SET body = EXCLUDED.body;

INSERT INTO notifications.notification_preferences (id, workspace_id, user_id, event_type, inbox, email) VALUES ('018f0000-0000-7000-8000-000000021001', '{{WorkspaceOpsId}}', '{{MemberId}}', 'CommentMentioned', true, true) ON CONFLICT (id) DO UPDATE SET inbox = EXCLUDED.inbox, email = EXCLUDED.email;
INSERT INTO notifications.notifications (id, workspace_id, recipient_user_id, event_type, entity_type, entity_id, payload, deduplication_key, created_at_utc) VALUES ('018f0000-0000-7000-8000-000000021101', '{{WorkspaceOpsId}}', '{{MemberId}}', 'CommentMentioned', 'Comment', '018f0000-0000-7000-8000-000000020001', '{"message":"Admin mentioned you in a seeded comment"}', 'seed-comment-mentioned', '2026-07-30T10:06:00Z') ON CONFLICT (id) DO UPDATE SET payload = EXCLUDED.payload;

INSERT INTO chat.channels (id, workspace_id, name, description, is_private, created_by_user_id, created_at_utc) VALUES ('018f0000-0000-7000-8000-000000022001', '{{WorkspaceOpsId}}', 'release-room', 'Seeded release coordination channel.', false, '{{OwnerId}}', '2026-07-01T00:00:00Z') ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, description = EXCLUDED.description;
INSERT INTO chat.channel_members (id, workspace_id, channel_id, user_id, joined_at_utc) VALUES ('018f0000-0000-7000-8000-000000022101', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000022001', '{{OwnerId}}', '2026-07-01T00:00:00Z'), ('018f0000-0000-7000-8000-000000022102', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000022001', '{{MemberId}}', '2026-07-01T00:00:00Z') ON CONFLICT (id) DO NOTHING;
INSERT INTO chat.messages (id, workspace_id, channel_id, parent_message_id, author_user_id, body, created_at_utc, is_deleted) VALUES ('018f0000-0000-7000-8000-000000022201', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000022001', NULL, '{{OwnerId}}', 'Release room is ready.', '2026-07-30T09:00:00Z', false), ('018f0000-0000-7000-8000-000000022202', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000022001', '018f0000-0000-7000-8000-000000022201', '{{MemberId}}', 'I will monitor QA fallout here.', '2026-07-30T09:05:00Z', false) ON CONFLICT (id) DO UPDATE SET body = EXCLUDED.body;

INSERT INTO time.time_policies (id, workspace_id, single_active_timer, rounding_minutes, minimum_duration_seconds, maximum_entry_seconds, billable_by_default, require_description, require_task, edit_window_hours, approval_required, week_starts_on, overtime_threshold_seconds) VALUES ('018f0000-0000-7000-8000-000000030001', '{{WorkspaceOpsId}}', true, 15, 300, 43200, true, true, false, 72, true, 1, 144000) ON CONFLICT (workspace_id) DO UPDATE SET rounding_minutes = EXCLUDED.rounding_minutes;
INSERT INTO time.member_rates (id, workspace_id, user_id, project_id, billing_rate, cost_rate) VALUES ('018f0000-0000-7000-8000-000000030101', '{{WorkspaceOpsId}}', '{{MemberId}}', NULL, 165.00, 72.00) ON CONFLICT (id) DO UPDATE SET billing_rate = EXCLUDED.billing_rate, cost_rate = EXCLUDED.cost_rate;
INSERT INTO time.time_entries (id, workspace_id, user_id, task_id, started_at_utc, ended_at_utc, duration_seconds, time_zone_id, description, is_billable, billing_rate, cost_rate, source, approval_status, created_at_utc) VALUES ('018f0000-0000-7000-8000-000000030201', '{{WorkspaceOpsId}}', '{{MemberId}}', '018f0000-0000-7000-8000-000000013001', '2026-07-29T09:00:00Z', '2026-07-29T11:30:00Z', 9000, 'Asia/Karachi', 'API integration planning', true, 165.00, 72.00, 'Manual', 'Approved', '2026-07-29T11:30:00Z') ON CONFLICT (id) DO UPDATE SET duration_seconds = EXCLUDED.duration_seconds;
INSERT INTO time.timesheet_periods (id, workspace_id, user_id, period_start_utc, period_end_utc, cadence, status, submitted_at_utc, approved_by_user_id, decided_at_utc) VALUES ('018f0000-0000-7000-8000-000000030301', '{{WorkspaceOpsId}}', '{{MemberId}}', '2026-07-27T00:00:00Z', '2026-08-03T00:00:00Z', 'Weekly', 'Approved', '2026-07-30T12:00:00Z', '{{AdminId}}', '2026-07-30T13:00:00Z') ON CONFLICT (workspace_id, user_id, period_start_utc) DO UPDATE SET status = EXCLUDED.status;

INSERT INTO planning.work_schedules (id, workspace_id, working_days_mask, daily_capacity_hours) VALUES ('018f0000-0000-7000-8000-000000040001', '{{WorkspaceOpsId}}', 62, 7.50) ON CONFLICT (workspace_id) DO UPDATE SET working_days_mask = EXCLUDED.working_days_mask;
INSERT INTO planning.holidays (id, workspace_id, date_utc, name) VALUES ('018f0000-0000-7000-8000-000000040101', '{{WorkspaceOpsId}}', '2026-08-14', 'Independence Day') ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;
INSERT INTO planning.leave_entries (id, workspace_id, user_id, start_date, end_date, type) VALUES ('018f0000-0000-7000-8000-000000040201', '{{WorkspaceOpsId}}', '{{MemberId}}', '2026-08-18', '2026-08-19', 'Vacation') ON CONFLICT (id) DO UPDATE SET type = EXCLUDED.type;
INSERT INTO planning.task_estimates (id, workspace_id, task_id, estimate_seconds, created_at_utc, updated_at_utc) VALUES ('018f0000-0000-7000-8000-000000040301', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000013001', 28800, '2026-07-30T00:00:00Z', '2026-07-30T00:00:00Z') ON CONFLICT (workspace_id, task_id) DO UPDATE SET estimate_seconds = EXCLUDED.estimate_seconds;
INSERT INTO planning.sprints (id, workspace_id, name, start_date, end_date, status, created_by_user_id, created_at_utc) VALUES ('018f0000-0000-7000-8000-000000040401', '{{WorkspaceOpsId}}', 'Remediation Sprint', '2026-07-30', '2026-08-13', 'Active', '{{OwnerId}}', '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET status = EXCLUDED.status;
INSERT INTO planning.sprint_items (id, workspace_id, sprint_id, task_id, points) VALUES ('018f0000-0000-7000-8000-000000040501', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000040401', '018f0000-0000-7000-8000-000000013001', 8) ON CONFLICT (id) DO UPDATE SET points = EXCLUDED.points;

INSERT INTO reporting.dashboards (id, workspace_id, name, is_private, owner_user_id, created_at_utc, updated_at_utc) VALUES ('018f0000-0000-7000-8000-000000050001', '{{WorkspaceOpsId}}', 'Remediation Dashboard', false, '{{OwnerId}}', '2026-07-30T00:00:00Z', '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;
INSERT INTO reporting.dashboard_widgets (id, workspace_id, dashboard_id, type, config_json, position) VALUES ('018f0000-0000-7000-8000-000000050101', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000050001', 'TasksByStatus', '{"title":"Tasks by status"}', 1) ON CONFLICT (id) DO UPDATE SET config_json = EXCLUDED.config_json;

INSERT INTO docs.documents (id, workspace_id, owner_user_id, title, content, is_private, space_id, list_id, task_id, created_at_utc, updated_at_utc) VALUES ('018f0000-0000-7000-8000-000000060001', '{{WorkspaceOpsId}}', '{{OwnerId}}', 'Remediation runbook', 'Use this seeded document to verify document APIs.', false, '018f0000-0000-7000-8000-000000011001', '018f0000-0000-7000-8000-000000012001', '018f0000-0000-7000-8000-000000013001', '2026-07-30T00:00:00Z', '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, content = EXCLUDED.content;
INSERT INTO docs.document_versions (id, workspace_id, document_id, author_user_id, content, created_at_utc) VALUES ('018f0000-0000-7000-8000-000000060101', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000060001', '{{OwnerId}}', 'Initial seeded version', '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET content = EXCLUDED.content;

INSERT INTO forms.forms (id, workspace_id, list_id, title, description, public_token, is_active, created_by_user_id, created_at_utc, updated_at_utc) VALUES ('018f0000-0000-7000-8000-000000061001', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000012001', 'Bug intake', 'Seeded public bug intake form.', 'seed-bug-intake', true, '{{OwnerId}}', '2026-07-30T00:00:00Z', '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET title = EXCLUDED.title, public_token = EXCLUDED.public_token;
INSERT INTO forms.form_fields (id, workspace_id, form_id, label, type, required, options_csv, position) VALUES ('018f0000-0000-7000-8000-000000061101', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000061001', 'Short title', 'Text', true, '', 1), ('018f0000-0000-7000-8000-000000061102', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000061001', 'Severity', 'Select', true, 'Low|Medium|High|Critical', 2) ON CONFLICT (id) DO UPDATE SET label = EXCLUDED.label, options_csv = EXCLUDED.options_csv;
INSERT INTO forms.form_submissions (id, workspace_id, form_id, created_task_id, values_json, idempotency_key, submitted_at_utc) VALUES ('018f0000-0000-7000-8000-000000061201', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000061001', '018f0000-0000-7000-8000-000000013002', '{"Short title":"Seeded keyboard issue","Severity":"High"}', 'seed-form-submission', '2026-07-30T11:00:00Z') ON CONFLICT (id) DO UPDATE SET values_json = EXCLUDED.values_json;

INSERT INTO automation.automation_rules (id, workspace_id, name, trigger_type, condition_json, action_json, is_enabled, created_by_user_id, created_at_utc, updated_at_utc) VALUES ('018f0000-0000-7000-8000-000000070001', '{{WorkspaceOpsId}}', 'Notify on critical intake', 'form.submitted', '{"severity":"Critical"}', '{"notify":"workspace-admins"}', true, '{{AdminId}}', '2026-07-30T00:00:00Z', '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET is_enabled = EXCLUDED.is_enabled;
INSERT INTO automation.automation_runs (id, workspace_id, rule_id, event_id, status, detail, occurred_at_utc) VALUES ('018f0000-0000-7000-8000-000000070101', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000070001', '018f0000-0000-7000-8000-000000061201', 'Success', 'Seeded automation sample run.', '2026-07-30T11:01:00Z') ON CONFLICT (id) DO UPDATE SET status = EXCLUDED.status;

INSERT INTO integrations.webhook_subscriptions (id, workspace_id, url, secret, event_types_csv, is_active, created_by_user_id, created_at_utc) VALUES ('018f0000-0000-7000-8000-000000071001', '{{WorkspaceOpsId}}', 'http://localhost:8025/planvexa-webhook-disabled', 'seeded-secret-hash-only', 'task.created,form.submitted', false, '{{AdminId}}', '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET is_active = false;
INSERT INTO integrations.webhook_deliveries (id, workspace_id, subscription_id, event_id, event_type, attempt, success, status_code, detail, occurred_at_utc) VALUES ('018f0000-0000-7000-8000-000000071101', '{{WorkspaceOpsId}}', '018f0000-0000-7000-8000-000000071001', '018f0000-0000-7000-8000-000000061201', 'form.submitted', 1, true, 202, 'Seeded disabled webhook sample.', '2026-07-30T11:02:00Z') ON CONFLICT (id) DO UPDATE SET success = EXCLUDED.success;
INSERT INTO integrations.personal_access_tokens (id, workspace_id, user_id, subject, email, display_name, name, token_hash, scopes_csv, expires_at_utc, created_at_utc) VALUES ('018f0000-0000-7000-8000-000000071201', '{{WorkspaceOpsId}}', '{{AdminId}}', '{{AdminSubject}}', 'admin@planvexa.local', 'Dev Admin', 'Seeded reporting token metadata', 'seeded-non-reusable-token-hash', 'tasks:read,reports:read', '2026-12-31T23:59:59Z', '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;


INSERT INTO governance.enterprise_security_settings (id, workspace_id, sso_enabled, scim_enabled, mfa_required, updated_at_utc) VALUES ('018f0000-0000-7000-8000-000000090001', '{{WorkspaceOpsId}}', false, false, false, '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET mfa_required = EXCLUDED.mfa_required;
INSERT INTO governance.retention_policies (id, workspace_id, deleted_task_retention_days, audit_retention_days, legal_hold, updated_at_utc) VALUES ('018f0000-0000-7000-8000-000000090101', '{{WorkspaceOpsId}}', 30, 365, false, '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET deleted_task_retention_days = EXCLUDED.deleted_task_retention_days;
INSERT INTO governance.export_jobs (id, workspace_id, dataset, requested_by_user_id, status, artifact, row_count, created_at_utc, completed_at_utc) VALUES ('018f0000-0000-7000-8000-000000090201', '{{WorkspaceOpsId}}', 'AuditLog', '{{AdminId}}', 'Completed', 'seed://exports/audit-log.csv', 3, '2026-07-30T00:00:00Z', '2026-07-30T00:05:00Z') ON CONFLICT (id) DO UPDATE SET status = EXCLUDED.status;

INSERT INTO ai.ai_requests (id, workspace_id, user_id, request_key, kind, entity_id, tokens_estimated, result, created_at_utc) VALUES ('018f0000-0000-7000-8000-0000000a0001', '{{WorkspaceOpsId}}', '{{MemberId}}', 'seed-ai-summary', 'Summary', '018f0000-0000-7000-8000-000000013001', 512, 'Seeded AI summary for remediation task.', '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET result = EXCLUDED.result;
INSERT INTO mobile.device_registrations (id, workspace_id, user_id, platform, token_hash, app_version, created_at_utc, last_seen_at_utc) VALUES ('018f0000-0000-7000-8000-0000000a0101', '{{WorkspaceOpsId}}', '{{MemberId}}', 'Web', 'seeded-device-token-hash', '0.1.0-dev', '2026-07-30T00:00:00Z', '2026-07-30T00:00:00Z') ON CONFLICT (id) DO UPDATE SET last_seen_at_utc = EXCLUDED.last_seen_at_utc;
""";
}
