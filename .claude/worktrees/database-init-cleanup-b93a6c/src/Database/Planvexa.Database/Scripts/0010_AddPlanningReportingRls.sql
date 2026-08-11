-- Planvexa DbUp script 0010_AddPlanningReportingRls.sql
-- Generated from EF Core migration 20260729145719_AddPlanningReportingRls. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

ALTER TABLE planning.work_schedules ENABLE ROW LEVEL SECURITY;

ALTER TABLE planning.work_schedules FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON planning.work_schedules
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE planning.holidays ENABLE ROW LEVEL SECURITY;

ALTER TABLE planning.holidays FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON planning.holidays
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE planning.leave_entries ENABLE ROW LEVEL SECURITY;

ALTER TABLE planning.leave_entries FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON planning.leave_entries
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE planning.task_estimates ENABLE ROW LEVEL SECURITY;

ALTER TABLE planning.task_estimates FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON planning.task_estimates
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE planning.sprints ENABLE ROW LEVEL SECURITY;

ALTER TABLE planning.sprints FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON planning.sprints
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE planning.sprint_items ENABLE ROW LEVEL SECURITY;

ALTER TABLE planning.sprint_items FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON planning.sprint_items
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE reporting.dashboards ENABLE ROW LEVEL SECURITY;

ALTER TABLE reporting.dashboards FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON reporting.dashboards
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE reporting.dashboard_widgets ENABLE ROW LEVEL SECURITY;

ALTER TABLE reporting.dashboard_widgets FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON reporting.dashboard_widgets
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
