-- Planvexa DbUp script 0062_AutomationsExpansion.sql
-- Automations expansion: nested condition groups, new trigger/action types, business-day
-- date math, rule versioning, dry-run, and bounded retry-then-dead-letter for failed runs.
--
-- automation.automation_rules: adds trigger_config_json (nullable jsonb -- scheduled/due-date trigger
-- config, e.g. {"everyMinutes":60}; only the new sweep-driven trigger types use it) and version (int,
-- defaulted to 1 for existing rows so every already-migrated rule starts at version 1, matching a
-- freshly-created rule).
--
-- automation.automation_runs: adds the triggering event's shape (event_type/entity_type/entity_id/
-- actor_user_id/data_json) so a Failed run can be reconstructed and retried without the original
-- (ephemeral, outbox-derived) WorkspaceEvent still being available, plus attempts/next_retry_at_utc for
-- the bounded backoff-retry sweep. Existing rows get safe defaults (event_type/entity_type = '' since
-- historical runs predate this column and are never retried; attempts = 1, matching a fresh run).
--
-- automation.automation_rule_versions: brand new table (rule-edit history), same
-- workspace_id NOT NULL + RLS shape used since 0029/0030 (see 0061's header for the exact pattern).
--
-- ADD COLUMN IF NOT EXISTS / CREATE TABLE IF NOT EXISTS: safe on both an empty database and the current
-- already-migrated dev database (AGENTS.md rule 9).

ALTER TABLE automation.automation_rules ADD COLUMN IF NOT EXISTS trigger_config_json jsonb;
ALTER TABLE automation.automation_rules ADD COLUMN IF NOT EXISTS version integer NOT NULL DEFAULT 1;

ALTER TABLE automation.automation_runs ADD COLUMN IF NOT EXISTS event_type character varying(64) NOT NULL DEFAULT '';
ALTER TABLE automation.automation_runs ADD COLUMN IF NOT EXISTS entity_type character varying(64) NOT NULL DEFAULT '';
ALTER TABLE automation.automation_runs ADD COLUMN IF NOT EXISTS entity_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
ALTER TABLE automation.automation_runs ADD COLUMN IF NOT EXISTS actor_user_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
ALTER TABLE automation.automation_runs ADD COLUMN IF NOT EXISTS data_json jsonb NOT NULL DEFAULT '{}';
ALTER TABLE automation.automation_runs ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 1;
ALTER TABLE automation.automation_runs ADD COLUMN IF NOT EXISTS next_retry_at_utc timestamp with time zone;

-- The retry sweep scans across every workspace for due retries (IgnoreQueryFilters), same shape as
-- 0061's ix_scheduled_reports_is_enabled partial-index pattern.
CREATE INDEX IF NOT EXISTS ix_automation_runs_status_next_retry_at_utc ON automation.automation_runs (status, next_retry_at_utc) WHERE status = 'Failed';

CREATE TABLE IF NOT EXISTS automation.automation_rule_versions (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    rule_id uuid NOT NULL,
    version integer NOT NULL,
    name character varying(200) NOT NULL,
    trigger_type character varying(64) NOT NULL,
    condition_json jsonb NOT NULL,
    action_json jsonb NOT NULL,
    trigger_config_json jsonb,
    changed_by_user_id uuid NOT NULL,
    changed_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_automation_rule_versions PRIMARY KEY (id),
    CONSTRAINT fk_automation_rule_versions_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_automation_rule_versions_automation_rules_rule_id FOREIGN KEY (rule_id) REFERENCES automation.automation_rules (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_automation_rule_versions_rule_id_version ON automation.automation_rule_versions (rule_id, version);

ALTER TABLE automation.automation_rule_versions ENABLE ROW LEVEL SECURITY;
ALTER TABLE automation.automation_rule_versions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON automation.automation_rule_versions;
CREATE POLICY workspace_isolation ON automation.automation_rule_versions USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
