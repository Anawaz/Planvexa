-- Planvexa DbUp script 0029_RemoveTenantIsolation.sql
-- ADR 0015: RLS phase of the final Tenant removal. Workspace becomes the sole
-- isolation/authorization boundary (AGENTS.md: "There is no Organization/Tenant layer").
--
-- Every table that previously carried a PERMISSIVE tenant_isolation policy (0002, 0019) already has
-- workspace_id NOT NULL and a RESTRICTIVE workspace_isolation policy layered under it (0024). With
-- Tenant removed there is nothing left for that RESTRICTIVE policy to be restrictive UNDER, so it is
-- recreated here as the sole PERMISSIVE policy per table, with the "ambient unset -> allow" escape
-- hatch removed (that hole was only safe while the tenant_isolation policy underneath still required
-- an exact tenant match). IF EXISTS / CREATE OR REPLACE-style guards throughout make this idempotent
-- on both a blank database and the current already-migrated dev database (AGENTS.md rule 9).
--
-- work.task_attachments (0021) and ai.provider_settings (0022) predate the RESTRICTIVE
-- workspace_isolation pattern (0024) and only ever got a PERMISSIVE tenant_isolation policy;
-- work.task_reminders (0028) rolled its own tenant_isolation + RESTRICTIVE workspace_isolation pair
-- directly. All three are included below alongside the main set so they end up with exactly the same
-- sole-PERMISSIVE workspace_isolation policy as every other workspace-owned table — otherwise, once
-- 0030 drops tenant_id (and CASCADE removes tenant_isolation with it), FORCE RLS + zero PERMISSIVE
-- policies would deny every row on these three tables.

-- ---------------------------------------------------------------------------------------------
-- 1. Drop every tenant-keyed policy, by name, wherever it was created (0001/0002/0019/0020).
-- ---------------------------------------------------------------------------------------------
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
        'work.tasks',
        'work.task_attachments',
        'ai.provider_settings',
        'work.task_reminders'
    ];
BEGIN
    FOREACH tenant_table IN ARRAY tenant_tables LOOP
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %s;', tenant_table);
    END LOOP;
END $$;

DROP POLICY IF EXISTS bootstrap_workspace_write ON tenancy.workspaces;
DROP POLICY IF EXISTS audit_isolation ON audit.audit_events;
DROP POLICY IF EXISTS bootstrap_audit_event_write ON audit.audit_events;
DROP POLICY IF EXISTS bootstrap_tenant_read ON tenancy.tenants;
-- Redundant with tenancy.feature_entitlements' own feature_entitlement_isolation policy (0001),
-- which already allows reads with no ambient workspace; this narrower bootstrap policy is superseded.
DROP POLICY IF EXISTS bootstrap_entitlement_read ON tenancy.feature_entitlements;

-- ---------------------------------------------------------------------------------------------
-- 2. Recreate workspace_isolation as the sole PERMISSIVE policy (no RESTRICTIVE layer left to
--    depend on), matching the hardened tenant_isolation pattern from 0019 (ambient value required,
--    exact match, no escape hatch).
-- ---------------------------------------------------------------------------------------------
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
        'work.tasks',
        'work.task_attachments',
        'ai.provider_settings',
        'work.task_reminders'
    ];
BEGIN
    FOREACH workspace_table IN ARRAY workspace_tables LOOP
        EXECUTE format('DROP POLICY IF EXISTS workspace_isolation ON %s;', workspace_table);
        EXECUTE format(
            'CREATE POLICY workspace_isolation ON %s USING (' ||
            'nullif(current_setting(''app.current_workspace'', true), '''') IS NOT NULL ' ||
            'AND workspace_id = nullif(current_setting(''app.current_workspace'', true), '''')::uuid' ||
            ') WITH CHECK (' ||
            'nullif(current_setting(''app.current_workspace'', true), '''') IS NOT NULL ' ||
            'AND workspace_id = nullif(current_setting(''app.current_workspace'', true), '''')::uuid' ||
            ');',
            workspace_table);
    END LOOP;
END $$;

-- ---------------------------------------------------------------------------------------------
-- 3. audit.audit_events and platform.outbox_messages never had a 1:1-derivable workspace: the
--    application allows a caller with an ambient Workspace to create ADDITIONAL Workspaces under
--    the same internal Tenant (see WorkspaceService.CreateAsync / apps/api ApiEndpoints.cs
--    "MapWorkspaces"), so the audited 1:1 Tenant<->Workspace invariant does NOT hold in general and
--    a historical tenant_id cannot be safely mapped to exactly one workspace_id. Both tables already
--    treat their owner column as optional ("platform-level events have no owner"); add workspace_id
--    as nullable with the same semantics instead of guessing. New rows are stamped from the ambient
--    Workspace context going forward (see AuditWriter / PlanvexaDbContext); existing rows keep a
--    NULL workspace_id, matching their existing NULL tenant_id semantics.
-- ---------------------------------------------------------------------------------------------
ALTER TABLE audit.audit_events ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE platform.outbox_messages ADD COLUMN IF NOT EXISTS workspace_id uuid;

CREATE POLICY audit_isolation ON audit.audit_events USING (
    nullif(current_setting('app.current_workspace', true), '') IS NULL
    OR workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NULL
    OR workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

-- Workspace creation is a bootstrap operation: the row does not exist yet, so it cannot be scoped by
-- an ambient workspace_id match. Require only that the connection has proven an identified app user
-- (set by the connection interceptor), same defence-in-depth strength as the tenant-scoped version
-- this replaces.
CREATE POLICY bootstrap_workspace_write ON tenancy.workspaces
FOR INSERT
WITH CHECK (
    nullif(current_setting('app.current_user', true), '') IS NOT NULL
);
