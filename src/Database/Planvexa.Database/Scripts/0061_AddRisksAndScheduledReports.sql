-- Planvexa DbUp script 0061_AddRisksAndScheduledReports.sql
-- Goals/OKRs + reporting completeness: reporting.risks (net-new portfolio risk register,
-- surfaced in PortfolioService's output alongside Milestones/Budget) and reporting.scheduled_reports
-- (periodic Dashboard export + email, driven by ScheduledReportBackgroundService). scope_id / dashboard_id
-- reference WorkManagement/Goals/Reporting entities by id only where they cross a module boundary (risk's
-- scope_id may be a Space, List or Goal id) -- no FK for those (AGENTS.md rule 7); dashboard_id DOES get
-- an FK since Dashboard is owned by this same Reporting schema. CREATE ... IF NOT EXISTS: safe on both an
-- empty database and the current already-migrated dev database (AGENTS.md rule 9) -- both tables are
-- brand new with no existing rows. Each gets its own workspace_id NOT NULL + sole workspace_isolation RLS
-- policy, the same pattern used since 0029/0030 (see 0059/0060's header).

CREATE TABLE IF NOT EXISTS reporting.risks (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    title character varying(200) NOT NULL,
    description character varying(4000),
    severity character varying(16) NOT NULL,
    scope_type character varying(16) NOT NULL,
    scope_id uuid NOT NULL,
    status character varying(16) NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_risks PRIMARY KEY (id),
    CONSTRAINT fk_risks_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_risks_workspace_id_scope_type_scope_id ON reporting.risks (workspace_id, scope_type, scope_id);

ALTER TABLE reporting.risks ENABLE ROW LEVEL SECURITY;
ALTER TABLE reporting.risks FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON reporting.risks;
CREATE POLICY workspace_isolation ON reporting.risks USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

CREATE TABLE IF NOT EXISTS reporting.scheduled_reports (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    dashboard_id uuid NOT NULL,
    recipients_csv character varying(4000) NOT NULL,
    cadence character varying(16) NOT NULL,
    is_enabled boolean NOT NULL DEFAULT true,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    last_sent_at_utc timestamp with time zone,
    CONSTRAINT pk_scheduled_reports PRIMARY KEY (id),
    CONSTRAINT fk_scheduled_reports_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_scheduled_reports_dashboards_dashboard_id FOREIGN KEY (dashboard_id) REFERENCES reporting.dashboards (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_scheduled_reports_workspace_id_dashboard_id ON reporting.scheduled_reports (workspace_id, dashboard_id);
-- The background scheduler polls across every workspace (IgnoreQueryFilters), same shape as
-- time.time_policies' missing-time-reminder scan -- an index on the enabled flag keeps that cheap.
CREATE INDEX IF NOT EXISTS ix_scheduled_reports_is_enabled ON reporting.scheduled_reports (is_enabled) WHERE is_enabled;

ALTER TABLE reporting.scheduled_reports ENABLE ROW LEVEL SECURITY;
ALTER TABLE reporting.scheduled_reports FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON reporting.scheduled_reports;
CREATE POLICY workspace_isolation ON reporting.scheduled_reports USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
