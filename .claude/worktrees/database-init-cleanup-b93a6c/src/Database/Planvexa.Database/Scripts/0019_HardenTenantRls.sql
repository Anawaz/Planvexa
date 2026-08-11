-- Harden tenant RLS for the normal application role.
-- Previous policies allowed unrestricted access when app.current_tenant was unset.
-- This change makes tenant context mandatory for tenant-owned tables; controlled
-- bootstrap/maintenance paths must use explicit application queries or a privileged admin connection.

DO $$
DECLARE
    tenant_table text;
    tenant_tables text[] := ARRAY[
        'ai.ai_requests',
        'audit.audit_events',
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
        'tenancy.workspaces',
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
    FOREACH tenant_table IN ARRAY tenant_tables LOOP
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %s;', tenant_table);
        EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY;', tenant_table);
        EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY;', tenant_table);
        EXECUTE format(
            'CREATE POLICY tenant_isolation ON %s USING (' ||
            'nullif(current_setting(''app.current_tenant'', true), '''') IS NOT NULL ' ||
            'AND tenant_id = nullif(current_setting(''app.current_tenant'', true), '''')::uuid' ||
            ') WITH CHECK (' ||
            'nullif(current_setting(''app.current_tenant'', true), '''') IS NOT NULL ' ||
            'AND tenant_id = nullif(current_setting(''app.current_tenant'', true), '''')::uuid' ||
            ');',
            tenant_table);
    END LOOP;
END $$;

