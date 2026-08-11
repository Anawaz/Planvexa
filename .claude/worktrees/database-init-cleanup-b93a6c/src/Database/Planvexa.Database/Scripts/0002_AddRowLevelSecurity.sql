-- Planvexa DbUp script 0002_AddRowLevelSecurity.sql
-- Generated from EF Core migration 20260728205211_AddRowLevelSecurity. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

ALTER TABLE tenancy.workspaces ENABLE ROW LEVEL SECURITY;

ALTER TABLE tenancy.workspaces FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON tenancy.workspaces
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

CREATE POLICY bootstrap_workspace_write ON tenancy.workspaces
FOR INSERT
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NOT NULL
    AND tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE tenancy.workspace_members ENABLE ROW LEVEL SECURITY;

ALTER TABLE tenancy.workspace_members FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON tenancy.workspace_members
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE tenancy.teams ENABLE ROW LEVEL SECURITY;

ALTER TABLE tenancy.teams FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON tenancy.teams
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE tenancy.team_members ENABLE ROW LEVEL SECURITY;

ALTER TABLE tenancy.team_members FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON tenancy.team_members
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE tenancy.invitations ENABLE ROW LEVEL SECURITY;

ALTER TABLE tenancy.invitations FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON tenancy.invitations
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

-- tenancy.feature_entitlements RLS is set up by the baseline script (policy feature_entitlement_isolation)
-- keyed on workspace_id/app.current_workspace; this table never had a tenant_id column to key on here.

ALTER TABLE audit.audit_events ENABLE ROW LEVEL SECURITY;

ALTER TABLE audit.audit_events FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON audit.audit_events
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

CREATE POLICY bootstrap_audit_event_write ON audit.audit_events
FOR INSERT
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NOT NULL
    AND tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
