-- Planvexa DbUp script 0014_AddBillingAndGovernanceRls.sql
-- Generated from EF Core migration 20260729195025_AddBillingAndGovernanceRls. EF migration history writes removed; DbUp journals this script in platform.schema_versions.
-- Billing policies removed: the Billing module and its tables no longer exist.

ALTER TABLE governance.enterprise_security_settings ENABLE ROW LEVEL SECURITY;

ALTER TABLE governance.enterprise_security_settings FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON governance.enterprise_security_settings
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE governance.export_jobs ENABLE ROW LEVEL SECURITY;

ALTER TABLE governance.export_jobs FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON governance.export_jobs
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
