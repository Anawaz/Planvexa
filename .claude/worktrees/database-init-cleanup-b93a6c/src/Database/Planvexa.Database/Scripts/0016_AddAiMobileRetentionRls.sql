-- Planvexa DbUp script 0016_AddAiMobileRetentionRls.sql
-- Generated from EF Core migration 20260730064520_AddAiMobileRetentionRls. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

ALTER TABLE ai.ai_requests ENABLE ROW LEVEL SECURITY;

ALTER TABLE ai.ai_requests FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON ai.ai_requests
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE mobile.device_registrations ENABLE ROW LEVEL SECURITY;

ALTER TABLE mobile.device_registrations FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON mobile.device_registrations
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE governance.retention_policies ENABLE ROW LEVEL SECURITY;

ALTER TABLE governance.retention_policies FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON governance.retention_policies
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
