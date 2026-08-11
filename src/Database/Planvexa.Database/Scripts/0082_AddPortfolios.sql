-- Planvexa DbUp script 0082_AddPortfolios.sql
-- Reporting: named, owned, curated Portfolios -- a Portfolio groups a chosen subset of the workspace's
-- Spaces (portfolio_members, id-only references per AGENTS.md rule 7) so PortfolioService.GetReportAsync
-- can compute the existing Health/Progress/Milestones/Risks/Budget rollup scoped to just those Spaces,
-- instead of every Space in the workspace (PortfolioService.GetAsync's pre-existing workspace-wide
-- version is kept as-is). Same Name/OwnerUserId/IsPrivate shape as reporting.dashboards
-- (0009_AddPlanningAndReporting.sql), plus Status/StartUtc/TargetEndUtc for portfolio-level tracking.
--
-- CREATE ... IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9) -- both tables are brand new with no existing rows to backfill. Each gets
-- its own workspace_id NOT NULL + sole workspace_isolation RLS policy, the same pattern used by every
-- workspace-owned table since 0029/0030 (see 0076_AddDocumentComments.sql's header for the identical shape).

CREATE TABLE IF NOT EXISTS reporting.portfolios (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    owner_user_id uuid NOT NULL,
    is_private boolean NOT NULL,
    status character varying(16) NOT NULL,
    start_utc timestamp with time zone,
    target_end_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_portfolios PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS reporting.portfolio_members (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    portfolio_id uuid NOT NULL,
    space_id uuid NOT NULL,
    CONSTRAINT pk_portfolio_members PRIMARY KEY (id),
    CONSTRAINT fk_portfolio_members_portfolios_portfolio_id FOREIGN KEY (portfolio_id) REFERENCES reporting.portfolios (id) ON DELETE CASCADE,
    CONSTRAINT ak_portfolio_members_portfolio_id_space_id UNIQUE (portfolio_id, space_id)
);

CREATE INDEX IF NOT EXISTS ix_portfolios_workspace_id ON reporting.portfolios (workspace_id);
CREATE INDEX IF NOT EXISTS ix_portfolio_members_portfolio_id ON reporting.portfolio_members (portfolio_id);
CREATE INDEX IF NOT EXISTS ix_portfolio_members_workspace_id ON reporting.portfolio_members (workspace_id);

ALTER TABLE reporting.portfolios ENABLE ROW LEVEL SECURITY;
ALTER TABLE reporting.portfolios FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON reporting.portfolios;
CREATE POLICY workspace_isolation ON reporting.portfolios USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

ALTER TABLE reporting.portfolio_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE reporting.portfolio_members FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON reporting.portfolio_members;
CREATE POLICY workspace_isolation ON reporting.portfolio_members USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
