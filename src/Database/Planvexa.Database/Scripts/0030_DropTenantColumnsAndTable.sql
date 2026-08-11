-- Planvexa DbUp script 0030_DropTenantColumnsAndTable.sql
-- ADR 0015: schema phase of the final Tenant removal. Every entity that carried both
-- TenantId (ITenantOwned) and WorkspaceId (IWorkspaceOwned) already has WorkspaceId populated
-- independently by PlanvexaDbContext.EnforceWorkspaceIsolation (stamped from the ambient Workspace
-- context, never derived from TenantId), so there is nothing left to backfill: this script only
-- removes the now-redundant tenant_id column and the composite (tenant_id, X) constraints/indexes
-- built on top of it, replacing each with a plain WorkspaceId-led equivalent per AGENTS.md rule 4.
--
-- The audited 1:1 Tenant<->Workspace invariant does NOT hold universally in this codebase --
-- WorkspaceService.CreateAsync deliberately lets a caller with an ambient Workspace create an
-- ADDITIONAL Workspace under the same internal Tenant (apps/api/.../ApiEndpoints.cs "MapWorkspaces":
-- "With an active Workspace context this adds another Workspace to the caller's account") -- so nothing
-- here derives a table's workspace_id FROM tenant_id; every dropped column was already redundant with
-- an independently-correct workspace_id on the same row.
--
-- Ordering: add the plain replacement foreign keys and indexes FIRST (they do not reference tenant_id,
-- so they survive the CASCADE below), THEN drop tenant_id CASCADE (which removes the old composite
-- (tenant_id, x) constraints/indexes that DO reference it), THEN drop tenancy.tenants itself.
-- IF EXISTS / IF NOT EXISTS throughout: safe on both a blank database and the current already-migrated
-- dev database (AGENTS.md rule 9).

-- ---------------------------------------------------------------------------------------------
-- 1. Replacement plain foreign keys (the composite tenant_id-led FKs below are dropped via CASCADE
--    in step 3; without a replacement several child tables would lose referential integrity, e.g.
--    work.tasks -> work.lists was ONLY enforced via the composite (tenant_id, list_id) FK).
-- ---------------------------------------------------------------------------------------------
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_workspace_members_workspaces_workspace_id') THEN
        ALTER TABLE tenancy.workspace_members ADD CONSTRAINT fk_workspace_members_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_teams_workspaces_workspace_id') THEN
        ALTER TABLE tenancy.teams ADD CONSTRAINT fk_teams_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_team_members_teams_team_id') THEN
        ALTER TABLE tenancy.team_members ADD CONSTRAINT fk_team_members_teams_team_id FOREIGN KEY (team_id) REFERENCES tenancy.teams (id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_invitations_workspaces_workspace_id') THEN
        ALTER TABLE tenancy.invitations ADD CONSTRAINT fk_invitations_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_custom_field_values_custom_field_definitions_definition_id') THEN
        ALTER TABLE work.custom_field_values ADD CONSTRAINT fk_custom_field_values_custom_field_definitions_definition_id FOREIGN KEY (definition_id) REFERENCES work.custom_field_definitions (id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_recurring_occurrences_recurring_task_definitions_definition_') THEN
        ALTER TABLE work.recurring_occurrences ADD CONSTRAINT fk_recurring_occurrences_recurring_task_definitions_definition_ FOREIGN KEY (definition_id) REFERENCES work.recurring_task_definitions (id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_folders_spaces_space_id') THEN
        ALTER TABLE work.folders ADD CONSTRAINT fk_folders_spaces_space_id FOREIGN KEY (space_id) REFERENCES work.spaces (id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_lists_spaces_space_id') THEN
        ALTER TABLE work.lists ADD CONSTRAINT fk_lists_spaces_space_id FOREIGN KEY (space_id) REFERENCES work.spaces (id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tasks_lists_list_id') THEN
        ALTER TABLE work.tasks ADD CONSTRAINT fk_tasks_lists_list_id FOREIGN KEY (list_id) REFERENCES work.lists (id) ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_task_tags_tags_tag_id') THEN
        ALTER TABLE work.task_tags ADD CONSTRAINT fk_task_tags_tags_tag_id FOREIGN KEY (tag_id) REFERENCES work.tags (id) ON DELETE CASCADE;
    END IF;
END $$;


-- ---------------------------------------------------------------------------------------------
-- 2. Replacement WorkspaceId-led indexes (AGENTS.md: "composite indexes beginning with WorkspaceId").
--    Several are a superset of an index already added by 0023 for child tables that only got
--    workspace_id then; the narrower one is harmless left in place (Postgres allows overlapping
--    indexes) and dropping it is not worth a second pass here.
-- ---------------------------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS ix_teams_workspace_id ON tenancy.teams (workspace_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_workspaces_slug ON tenancy.workspaces (slug);
CREATE UNIQUE INDEX IF NOT EXISTS ux_workspace_members_workspace_id_user_id ON tenancy.workspace_members (workspace_id, user_id);
CREATE INDEX IF NOT EXISTS ix_custom_field_definitions_workspace_id ON work.custom_field_definitions (workspace_id);
CREATE INDEX IF NOT EXISTS ix_custom_field_options_workspace_id_definition_id ON work.custom_field_options (workspace_id, definition_id);
CREATE INDEX IF NOT EXISTS ix_custom_field_values_workspace_id_definition_id_date_value ON work.custom_field_values (workspace_id, definition_id, date_value);
CREATE INDEX IF NOT EXISTS ix_custom_field_values_workspace_id_definition_id_number_value ON work.custom_field_values (workspace_id, definition_id, number_value);
CREATE INDEX IF NOT EXISTS ix_custom_field_values_workspace_id_definition_id_option_id ON work.custom_field_values (workspace_id, definition_id, option_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_custom_field_values_workspace_id_task_id_definition_id ON work.custom_field_values (workspace_id, task_id, definition_id);
CREATE INDEX IF NOT EXISTS ix_folders_workspace_id_space_id ON work.folders (workspace_id, space_id);
CREATE INDEX IF NOT EXISTS ix_lists_workspace_id_space_id ON work.lists (workspace_id, space_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_recurring_occurrences_workspace_id_definition_id_occurrence_ ON work.recurring_occurrences (workspace_id, definition_id, occurrence_key);
CREATE INDEX IF NOT EXISTS ix_recurring_task_definitions_workspace_id_list_id ON work.recurring_task_definitions (workspace_id, list_id);
CREATE INDEX IF NOT EXISTS ix_saved_views_workspace_id ON work.saved_views (workspace_id);
CREATE INDEX IF NOT EXISTS ix_spaces_workspace_id ON work.spaces (workspace_id);
CREATE INDEX IF NOT EXISTS ix_status_schemes_workspace_id ON work.status_schemes (workspace_id);
CREATE INDEX IF NOT EXISTS ix_statuses_workspace_id_scheme_id ON work.statuses (workspace_id, scheme_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tags_workspace_id_name ON work.tags (workspace_id, name);
CREATE INDEX IF NOT EXISTS ix_task_activity_events_workspace_id_task_id_created_at_utc ON work.task_activity_events (workspace_id, task_id, created_at_utc);
CREATE UNIQUE INDEX IF NOT EXISTS ux_task_assignees_workspace_id_task_id_user_id ON work.task_assignees (workspace_id, task_id, user_id);
CREATE INDEX IF NOT EXISTS ix_task_assignees_workspace_id_user_id ON work.task_assignees (workspace_id, user_id);
CREATE INDEX IF NOT EXISTS ix_task_checklist_items_workspace_id_checklist_id ON work.task_checklist_items (workspace_id, checklist_id);
CREATE INDEX IF NOT EXISTS ix_task_checklists_workspace_id_task_id ON work.task_checklists (workspace_id, task_id);
CREATE INDEX IF NOT EXISTS ix_task_dependencies_workspace_id_depends_on_task_id ON work.task_dependencies (workspace_id, depends_on_task_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_task_dependencies_workspace_id_task_id_depends_on_task_id_ty ON work.task_dependencies (workspace_id, task_id, depends_on_task_id, type);
CREATE INDEX IF NOT EXISTS ix_task_tags_workspace_id_tag_id ON work.task_tags (workspace_id, tag_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_task_tags_workspace_id_task_id_tag_id ON work.task_tags (workspace_id, task_id, tag_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_task_watchers_workspace_id_task_id_user_id ON work.task_watchers (workspace_id, task_id, user_id);
CREATE INDEX IF NOT EXISTS ix_tasks_workspace_id_list_id_position ON work.tasks (workspace_id, list_id, position);
CREATE INDEX IF NOT EXISTS ix_tasks_workspace_id_list_id_status_id ON work.tasks (workspace_id, list_id, status_id);
CREATE INDEX IF NOT EXISTS ix_tasks_workspace_id_parent_id ON work.tasks (workspace_id, parent_id);
CREATE INDEX IF NOT EXISTS ix_tasks_workspace_id ON work.tasks (workspace_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_comment_reactions_workspace_id_comment_id_user_id_emoji ON collab.comment_reactions (workspace_id, comment_id, user_id, emoji);
CREATE INDEX IF NOT EXISTS ix_comments_workspace_id_parent_id ON collab.comments (workspace_id, parent_id);
CREATE INDEX IF NOT EXISTS ix_comments_workspace_id_task_id_created_at_utc ON collab.comments (workspace_id, task_id, created_at_utc);
CREATE UNIQUE INDEX IF NOT EXISTS ux_mentions_workspace_id_comment_id_mentioned_user_id ON collab.mentions (workspace_id, comment_id, mentioned_user_id);
CREATE INDEX IF NOT EXISTS ix_mentions_workspace_id_mentioned_user_id ON collab.mentions (workspace_id, mentioned_user_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_notification_preferences_workspace_id_user_id_event_type ON notifications.notification_preferences (workspace_id, user_id, event_type);
CREATE INDEX IF NOT EXISTS ix_notifications_workspace_id_recipient_user_id_created_at_utc ON notifications.notifications (workspace_id, recipient_user_id, created_at_utc);
CREATE UNIQUE INDEX IF NOT EXISTS ux_notifications_workspace_id_recipient_user_id_deduplication_k ON notifications.notifications (workspace_id, recipient_user_id, deduplication_key);
CREATE INDEX IF NOT EXISTS ix_notifications_workspace_id_recipient_user_id_read_at_utc ON notifications.notifications (workspace_id, recipient_user_id, read_at_utc);
CREATE INDEX IF NOT EXISTS ix_share_links_workspace_id_task_id ON sharing.share_links (workspace_id, task_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_member_rates_workspace_id_user_id_project_id ON time.member_rates (workspace_id, user_id, project_id);
CREATE INDEX IF NOT EXISTS ix_time_entries_workspace_id_task_id ON time.time_entries (workspace_id, task_id);
CREATE INDEX IF NOT EXISTS ix_time_entries_workspace_id_user_id_started_at_utc ON time.time_entries (workspace_id, user_id, started_at_utc);
CREATE INDEX IF NOT EXISTS ix_time_entries_workspace_id_started_at_utc ON time.time_entries (workspace_id, started_at_utc);
CREATE UNIQUE INDEX IF NOT EXISTS ux_time_entries_workspace_id_user_id ON time.time_entries (workspace_id, user_id) WHERE ended_at_utc IS NULL;
CREATE INDEX IF NOT EXISTS ix_time_entry_audits_workspace_id_time_entry_id_created_at_utc ON time.time_entry_audits (workspace_id, time_entry_id, created_at_utc);
CREATE UNIQUE INDEX IF NOT EXISTS ux_time_policies_workspace_id ON time.time_policies (workspace_id);
CREATE INDEX IF NOT EXISTS ix_timesheet_approvals_workspace_id_period_id ON time.timesheet_approvals (workspace_id, period_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_timesheet_periods_workspace_id_user_id_period_start_utc ON time.timesheet_periods (workspace_id, user_id, period_start_utc);
CREATE INDEX IF NOT EXISTS ix_dashboard_widgets_workspace_id_dashboard_id ON reporting.dashboard_widgets (workspace_id, dashboard_id);
CREATE INDEX IF NOT EXISTS ix_dashboards_workspace_id ON reporting.dashboards (workspace_id);
CREATE INDEX IF NOT EXISTS ix_holidays_workspace_id_date_utc ON planning.holidays (workspace_id, date_utc);
CREATE INDEX IF NOT EXISTS ix_leave_entries_workspace_id_user_id_start_date ON planning.leave_entries (workspace_id, user_id, start_date);
CREATE UNIQUE INDEX IF NOT EXISTS ux_sprint_items_workspace_id_sprint_id_task_id ON planning.sprint_items (workspace_id, sprint_id, task_id);
CREATE INDEX IF NOT EXISTS ix_sprints_workspace_id ON planning.sprints (workspace_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_task_estimates_workspace_id_task_id ON planning.task_estimates (workspace_id, task_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_work_schedules_workspace_id ON planning.work_schedules (workspace_id);
CREATE INDEX IF NOT EXISTS ix_automation_rules_workspace_id_trigger_type_is_enabled ON automation.automation_rules (workspace_id, trigger_type, is_enabled);
CREATE UNIQUE INDEX IF NOT EXISTS ux_automation_runs_workspace_id_rule_id_event_id ON automation.automation_runs (workspace_id, rule_id, event_id);
CREATE INDEX IF NOT EXISTS ix_automation_runs_workspace_id_occurred_at_utc ON automation.automation_runs (workspace_id, occurred_at_utc);
CREATE INDEX IF NOT EXISTS ix_document_versions_workspace_id_document_id_created_at_utc ON docs.document_versions (workspace_id, document_id, created_at_utc);
CREATE INDEX IF NOT EXISTS ix_documents_workspace_id ON docs.documents (workspace_id);
CREATE INDEX IF NOT EXISTS ix_form_fields_workspace_id_form_id ON forms.form_fields (workspace_id, form_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_form_submissions_workspace_id_form_id_idempotency_key ON forms.form_submissions (workspace_id, form_id, idempotency_key);
CREATE INDEX IF NOT EXISTS ix_form_submissions_workspace_id_form_id_submitted_at_utc ON forms.form_submissions (workspace_id, form_id, submitted_at_utc);
CREATE INDEX IF NOT EXISTS ix_forms_workspace_id ON forms.forms (workspace_id);
CREATE INDEX IF NOT EXISTS ix_personal_access_tokens_workspace_id_user_id ON integrations.personal_access_tokens (workspace_id, user_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_webhook_deliveries_workspace_id_subscription_id_event_id ON integrations.webhook_deliveries (workspace_id, subscription_id, event_id);
CREATE INDEX IF NOT EXISTS ix_webhook_deliveries_workspace_id_subscription_id_occurred_at_ ON integrations.webhook_deliveries (workspace_id, subscription_id, occurred_at_utc);
CREATE INDEX IF NOT EXISTS ix_webhook_subscriptions_workspace_id_is_active ON integrations.webhook_subscriptions (workspace_id, is_active);
CREATE UNIQUE INDEX IF NOT EXISTS ux_enterprise_security_settings_workspace_id ON governance.enterprise_security_settings (workspace_id);
CREATE INDEX IF NOT EXISTS ix_export_jobs_workspace_id_status ON governance.export_jobs (workspace_id, status);
CREATE INDEX IF NOT EXISTS ix_export_jobs_workspace_id_created_at_utc ON governance.export_jobs (workspace_id, created_at_utc);
CREATE INDEX IF NOT EXISTS ix_ai_requests_workspace_id_created_at_utc ON ai.ai_requests (workspace_id, created_at_utc);
CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_requests_workspace_id_request_key ON ai.ai_requests (workspace_id, request_key);
CREATE INDEX IF NOT EXISTS ix_device_registrations_workspace_id_user_id ON mobile.device_registrations (workspace_id, user_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_device_registrations_workspace_id_user_id_token_hash ON mobile.device_registrations (workspace_id, user_id, token_hash);
CREATE UNIQUE INDEX IF NOT EXISTS ux_retention_policies_workspace_id ON governance.retention_policies (workspace_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_channel_members_workspace_id_channel_id_user_id ON chat.channel_members (workspace_id, channel_id, user_id);
CREATE INDEX IF NOT EXISTS ix_channels_workspace_id ON chat.channels (workspace_id);
CREATE INDEX IF NOT EXISTS ix_messages_workspace_id_channel_id_created_at_utc ON chat.messages (workspace_id, channel_id, created_at_utc);
CREATE INDEX IF NOT EXISTS ix_messages_workspace_id_parent_message_id ON chat.messages (workspace_id, parent_message_id);
CREATE INDEX IF NOT EXISTS ix_task_attachments_workspace_id_task_id ON work.task_attachments (workspace_id, task_id);
CREATE INDEX IF NOT EXISTS ix_folders_workspace_id_parent_folder_id ON work.folders (workspace_id, parent_folder_id);
CREATE INDEX IF NOT EXISTS ix_task_reminders_workspace_id_task_id ON work.task_reminders (workspace_id, task_id);

-- ---------------------------------------------------------------------------------------------
-- 3. Drop tenant_id from every table that still has it. CASCADE removes the composite
--    (tenant_id, x) constraints/indexes built on it (already superseded by step 1/2 above) along
--    with the plain tenant_id-only indexes/alternate keys that have no workspace equivalent to
--    replace (they existed only to support tenant-scoped lookups that no longer exist).
-- ---------------------------------------------------------------------------------------------
ALTER TABLE audit.audit_events DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE platform.outbox_messages DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE tenancy.workspaces DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE tenancy.workspace_members DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE tenancy.teams DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE tenancy.team_members DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE tenancy.invitations DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.custom_field_definitions DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.recurring_task_definitions DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.saved_views DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.spaces DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.status_schemes DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.tags DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.task_activity_events DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.task_checklists DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.task_dependencies DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.custom_field_options DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.custom_field_values DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.recurring_occurrences DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.folders DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.lists DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.statuses DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.task_checklist_items DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.tasks DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.task_assignees DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.task_tags DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.task_watchers DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE collab.comments DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE notifications.notification_preferences DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE notifications.notifications DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE sharing.share_links DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE collab.comment_reactions DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE collab.mentions DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE notifications.notification_deliveries DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE time.member_rates DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE time.time_entries DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE time.time_entry_audits DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE time.time_policies DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE time.timesheet_periods DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE time.timesheet_approvals DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE reporting.dashboards DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE planning.holidays DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE planning.leave_entries DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE planning.sprints DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE planning.task_estimates DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE planning.work_schedules DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE reporting.dashboard_widgets DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE planning.sprint_items DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE automation.automation_rules DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE automation.automation_runs DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE docs.documents DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE forms.form_submissions DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE forms.forms DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE integrations.personal_access_tokens DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE integrations.webhook_deliveries DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE integrations.webhook_subscriptions DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE docs.document_versions DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE forms.form_fields DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE governance.enterprise_security_settings DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE governance.export_jobs DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE ai.ai_requests DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE mobile.device_registrations DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE governance.retention_policies DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE chat.channels DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE chat.messages DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE chat.channel_members DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.task_attachments DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE ai.provider_settings DROP COLUMN IF EXISTS tenant_id CASCADE;
ALTER TABLE work.task_reminders DROP COLUMN IF EXISTS tenant_id CASCADE;

-- ---------------------------------------------------------------------------------------------
-- 4. Drop the Tenant aggregate's table itself. IF EXISTS handles an already-migrated database
--    where a previous run of this script (or a manual cleanup) already removed it.
-- ---------------------------------------------------------------------------------------------
DROP TABLE IF EXISTS tenancy.tenants CASCADE;
