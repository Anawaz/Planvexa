-- Planvexa DbUp script 0070_AddWorkspaceIpAllowRules.sql
-- Per-workspace IP allow list. governance.workspace_ip_allow_rules (brand new table) --
-- one row per allowed CIDR range; a workspace with zero rows is unrestricted (checked by
-- IpAllowListService.IsAllowedAsync / enforced by apps/api's IpAllowListMiddleware, same
-- "empty configuration = no restriction" convention as every other optional workspace security feature).
--
-- All CREATE ... IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9) -- brand new table, no existing rows to backfill. workspace_id NOT NULL +
-- sole workspace_isolation RLS policy, the same pattern every workspace-owned table has used since
-- 0029/0030 (see 0068's header for the identical shape).

CREATE TABLE IF NOT EXISTS governance.workspace_ip_allow_rules (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    cidr character varying(64) NOT NULL,
    description character varying(200),
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_workspace_ip_allow_rules PRIMARY KEY (id),
    CONSTRAINT fk_workspace_ip_allow_rules_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_workspace_ip_allow_rules_workspace_id ON governance.workspace_ip_allow_rules (workspace_id);

ALTER TABLE governance.workspace_ip_allow_rules ENABLE ROW LEVEL SECURITY;
ALTER TABLE governance.workspace_ip_allow_rules FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON governance.workspace_ip_allow_rules;
CREATE POLICY workspace_isolation ON governance.workspace_ip_allow_rules USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
