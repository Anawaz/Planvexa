-- Planvexa DbUp script 0031_AddRolesAndRolePermissions.sql
-- ADR-0003: DB-backed role/permission model foundation. tenancy.roles holds the five built-in
-- roles seeded per workspace (owner/admin/member/limited_member/guest) plus room for a future custom
-- role; tenancy.role_permissions holds each role's granted permission keys. Workspace is the sole
-- isolation boundary (AGENTS.md) -- both tables carry workspace_id NOT NULL and the same
-- sole-PERMISSIVE workspace_isolation RLS policy as every other workspace-owned table in the
-- post-0029/0030 hardened shape (ambient app.current_workspace required, exact match, no escape
-- hatch). Seeding existing workspaces' rows and backfilling workspace_members.role_id are separate
-- scripts (0032, 0033) so this one only creates the schema -- IF NOT EXISTS/IF EXISTS guards make it
-- safe on both an empty database and the current already-migrated dev database (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS tenancy.roles (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    key character varying(64) NOT NULL,
    name character varying(100) NOT NULL,
    is_built_in boolean NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_roles PRIMARY KEY (id),
    CONSTRAINT fk_roles_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_workspace_id_key ON tenancy.roles (workspace_id, key);

ALTER TABLE tenancy.roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy.roles FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON tenancy.roles;
CREATE POLICY workspace_isolation ON tenancy.roles USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

-- workspace_id is denormalized here (rather than requiring a join through roles) for direct RLS
-- scoping and a workspace-led index, matching the pattern used by other workspace-owned join tables
-- (e.g. work.task_tags).
CREATE TABLE IF NOT EXISTS tenancy.role_permissions (
    role_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    permission_key character varying(64) NOT NULL,
    CONSTRAINT pk_role_permissions PRIMARY KEY (role_id, permission_key),
    CONSTRAINT fk_role_permissions_roles_role_id FOREIGN KEY (role_id) REFERENCES tenancy.roles (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_role_permissions_workspace_id ON tenancy.role_permissions (workspace_id);

ALTER TABLE tenancy.role_permissions ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy.role_permissions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON tenancy.role_permissions;
CREATE POLICY workspace_isolation ON tenancy.role_permissions USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
