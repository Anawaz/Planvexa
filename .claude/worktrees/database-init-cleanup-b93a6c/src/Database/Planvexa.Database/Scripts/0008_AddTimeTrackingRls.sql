-- Planvexa DbUp script 0008_AddTimeTrackingRls.sql
-- Generated from EF Core migration 20260729142124_AddTimeTrackingRls. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

ALTER TABLE time.time_entries ENABLE ROW LEVEL SECURITY;

ALTER TABLE time.time_entries FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON time.time_entries
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE time.time_entry_audits ENABLE ROW LEVEL SECURITY;

ALTER TABLE time.time_entry_audits FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON time.time_entry_audits
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE time.time_policies ENABLE ROW LEVEL SECURITY;

ALTER TABLE time.time_policies FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON time.time_policies
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE time.member_rates ENABLE ROW LEVEL SECURITY;

ALTER TABLE time.member_rates FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON time.member_rates
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE time.timesheet_periods ENABLE ROW LEVEL SECURITY;

ALTER TABLE time.timesheet_periods FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON time.timesheet_periods
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE time.timesheet_approvals ENABLE ROW LEVEL SECURITY;

ALTER TABLE time.timesheet_approvals FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON time.timesheet_approvals
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
