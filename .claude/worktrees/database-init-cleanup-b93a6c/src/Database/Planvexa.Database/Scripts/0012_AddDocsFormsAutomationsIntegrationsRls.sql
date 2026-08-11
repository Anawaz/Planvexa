-- Planvexa DbUp script 0012_AddDocsFormsAutomationsIntegrationsRls.sql
-- Generated from EF Core migration 20260729185137_AddDocsFormsAutomationsIntegrationsRls. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

ALTER TABLE docs.documents ENABLE ROW LEVEL SECURITY;

ALTER TABLE docs.documents FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON docs.documents
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE docs.document_versions ENABLE ROW LEVEL SECURITY;

ALTER TABLE docs.document_versions FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON docs.document_versions
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE forms.forms ENABLE ROW LEVEL SECURITY;

ALTER TABLE forms.forms FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON forms.forms
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE forms.form_fields ENABLE ROW LEVEL SECURITY;

ALTER TABLE forms.form_fields FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON forms.form_fields
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE forms.form_submissions ENABLE ROW LEVEL SECURITY;

ALTER TABLE forms.form_submissions FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON forms.form_submissions
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE automation.automation_rules ENABLE ROW LEVEL SECURITY;

ALTER TABLE automation.automation_rules FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON automation.automation_rules
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE automation.automation_runs ENABLE ROW LEVEL SECURITY;

ALTER TABLE automation.automation_runs FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON automation.automation_runs
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE integrations.webhook_subscriptions ENABLE ROW LEVEL SECURITY;

ALTER TABLE integrations.webhook_subscriptions FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON integrations.webhook_subscriptions
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE integrations.webhook_deliveries ENABLE ROW LEVEL SECURITY;

ALTER TABLE integrations.webhook_deliveries FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON integrations.webhook_deliveries
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE integrations.personal_access_tokens ENABLE ROW LEVEL SECURITY;

ALTER TABLE integrations.personal_access_tokens FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON integrations.personal_access_tokens
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
