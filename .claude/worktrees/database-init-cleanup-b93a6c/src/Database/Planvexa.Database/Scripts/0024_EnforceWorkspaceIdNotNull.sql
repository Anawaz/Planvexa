-- Planvexa DbUp script 0024_EnforceWorkspaceIdNotNull.sql
-- ADR 0015: contract phase of the Tenant->Workspace migration.
--
-- Locks workspace_id NOT NULL on the workspace-owned CHILD tables. Safe because:
--   * 0023 already backfilled every existing child row from its parent (validated by that script's
--     guard), so existing data satisfies the constraint on an upgraded database;
--   * a blank database has no rows;
--   * new EF writes are stamped by EnforceWorkspaceIsolation, and the dev seeder now supplies
--     workspace_id inline, so future rows satisfy it.
-- SET NOT NULL is idempotent (a no-op on an already-NOT-NULL column). tenant_id is retained until the final removal script.

ALTER TABLE work.task_checklists       ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE work.task_dependencies     ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE work.task_checklist_items  ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE work.custom_field_options  ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE work.custom_field_values   ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE work.recurring_occurrences ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE work.statuses              ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE work.task_assignees        ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE work.task_tags             ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE work.task_watchers         ALTER COLUMN workspace_id SET NOT NULL;

ALTER TABLE collab.comment_reactions   ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE collab.mentions            ALTER COLUMN workspace_id SET NOT NULL;

ALTER TABLE notifications.notification_deliveries ALTER COLUMN workspace_id SET NOT NULL;

ALTER TABLE time.time_entry_audits     ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE time.timesheet_approvals   ALTER COLUMN workspace_id SET NOT NULL;

ALTER TABLE reporting.dashboard_widgets ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE planning.sprint_items      ALTER COLUMN workspace_id SET NOT NULL;

ALTER TABLE docs.document_versions     ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE forms.form_fields          ALTER COLUMN workspace_id SET NOT NULL;

ALTER TABLE chat.channel_members       ALTER COLUMN workspace_id SET NOT NULL;

-- Genuine workspace-level RLS isolation, added here (not 0019) because every table below needs
-- workspace_id to already exist and be populated — 0019 runs before this script's own backfill.
-- A RESTRICTIVE policy ANDs with the permissive tenant_isolation from 0019, so a row is
-- visible/writable only when it matches BOTH the current tenant AND the current workspace. Two
-- Workspaces under the same Tenant were previously distinguishable only by application-level query
-- filters, not RLS. tenancy.workspaces is excluded: it's the tenant-level collection of workspaces
-- itself (PlanvexaDbContext.ApplyWorkspaceQueryFilters excludes it for the same reason — listing "my
-- workspaces" has no single ambient workspace to filter by). audit.audit_events is excluded too: it
-- has no workspace_id column (even tenant_id is nullable there — some audit actions are genuinely
-- written without full context).
DO $$
DECLARE
    workspace_table text;
    workspace_tables text[] := ARRAY[
        'ai.ai_requests',
        'automation.automation_rules',
        'automation.automation_runs',
        'chat.channel_members',
        'chat.channels',
        'chat.messages',
        'collab.comment_reactions',
        'collab.comments',
        'collab.mentions',
        'docs.document_versions',
        'docs.documents',
        'forms.form_fields',
        'forms.form_submissions',
        'forms.forms',
        'governance.enterprise_security_settings',
        'governance.export_jobs',
        'governance.retention_policies',
        'integrations.personal_access_tokens',
        'integrations.webhook_deliveries',
        'integrations.webhook_subscriptions',
        'mobile.device_registrations',
        'notifications.notification_deliveries',
        'notifications.notification_preferences',
        'notifications.notifications',
        'planning.holidays',
        'planning.leave_entries',
        'planning.sprint_items',
        'planning.sprints',
        'planning.task_estimates',
        'planning.work_schedules',
        'reporting.dashboard_widgets',
        'reporting.dashboards',
        'sharing.share_links',
        'tenancy.invitations',
        'tenancy.team_members',
        'tenancy.teams',
        'tenancy.workspace_members',
        'time.member_rates',
        'time.time_entries',
        'time.time_entry_audits',
        'time.time_policies',
        'time.timesheet_approvals',
        'time.timesheet_periods',
        'work.custom_field_definitions',
        'work.custom_field_options',
        'work.custom_field_values',
        'work.folders',
        'work.lists',
        'work.recurring_occurrences',
        'work.recurring_task_definitions',
        'work.saved_views',
        'work.spaces',
        'work.status_schemes',
        'work.statuses',
        'work.tags',
        'work.task_activity_events',
        'work.task_assignees',
        'work.task_checklist_items',
        'work.task_checklists',
        'work.task_dependencies',
        'work.task_tags',
        'work.task_watchers',
        'work.tasks'
    ];
BEGIN
    FOREACH workspace_table IN ARRAY workspace_tables LOOP
        EXECUTE format('DROP POLICY IF EXISTS workspace_isolation ON %s;', workspace_table);
        EXECUTE format(
            'CREATE POLICY workspace_isolation ON %s AS RESTRICTIVE USING (' ||
            'nullif(current_setting(''app.current_workspace'', true), '''') IS NULL ' ||
            'OR workspace_id = nullif(current_setting(''app.current_workspace'', true), '''')::uuid' ||
            ') WITH CHECK (' ||
            'nullif(current_setting(''app.current_workspace'', true), '''') IS NULL ' ||
            'OR workspace_id = nullif(current_setting(''app.current_workspace'', true), '''')::uuid' ||
            ');',
            workspace_table);
    END LOOP;
END $$;
