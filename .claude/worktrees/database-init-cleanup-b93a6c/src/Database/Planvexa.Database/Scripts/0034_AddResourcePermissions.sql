-- Planvexa DbUp script 0034_AddResourcePermissions.sql
-- ADR-0003: per-resource ACL. tenancy.resource_permissions grants a permission_level to a
-- principal (user/team/role) on one resource, identified by (resource_type, resource_id). resource_type
-- is a free-form string (starts with space/folder/list/task) so later changes can add their own resource
-- types without a schema change. Follows the exact workspace_id NOT NULL + sole workspace_isolation RLS
-- policy pattern used by every workspace-owned table since 0029/0030 (see 0031's header). IF NOT
-- EXISTS/IF EXISTS guards make this safe on both an empty database and the current already-migrated
-- dev database (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS tenancy.resource_permissions (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    resource_type character varying(64) NOT NULL,
    resource_id uuid NOT NULL,
    principal_type character varying(16) NOT NULL,
    principal_id uuid NOT NULL,
    permission_level character varying(16) NOT NULL,
    granted_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_resource_permissions PRIMARY KEY (id),
    CONSTRAINT fk_resource_permissions_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT ck_resource_permissions_principal_type CHECK (principal_type IN ('user', 'team', 'role')),
    CONSTRAINT ck_resource_permissions_permission_level CHECK (permission_level IN ('view', 'comment', 'edit', 'full_edit', 'share', 'manage'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_resource_permissions_resource_principal
    ON tenancy.resource_permissions (resource_type, resource_id, principal_type, principal_id);

CREATE INDEX IF NOT EXISTS ix_resource_permissions_workspace_resource
    ON tenancy.resource_permissions (workspace_id, resource_type, resource_id);

CREATE INDEX IF NOT EXISTS ix_resource_permissions_workspace_principal
    ON tenancy.resource_permissions (workspace_id, principal_type, principal_id);

ALTER TABLE tenancy.resource_permissions ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy.resource_permissions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON tenancy.resource_permissions;
CREATE POLICY workspace_isolation ON tenancy.resource_permissions USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
