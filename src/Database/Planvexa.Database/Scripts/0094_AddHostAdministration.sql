-- Planvexa DbUp script 0094_AddHostAdministration.sql
-- Host administration: the instance-level (not Workspace-level) admin role for whoever runs this
-- Planvexa installation. Workspace stays the sole isolation boundary for CONTENT -- a host admin sees
-- workspace/user/membership METADATA across the instance, never tasks, documents or messages.
--
-- identity.users.is_host_admin: the grant itself. identity.users is a global table with no RLS (0001,
-- see also the note in 0075), so it is readable from any connection -- which is exactly what lets the
-- policies below re-validate the flag inside the database rather than trusting the application layer
-- alone. Defaults to false, so an upgraded database grants nobody anything until either
-- PlanvexaBootstrap promotes the configured Bootstrap:AdminSubject on the next start, or an existing
-- host admin grants it through the panel.
--
-- Why new policies instead of ConnectionStrings:PlanvexaMaintenance (MaintenanceConnection): that
-- connection is OPTIONAL and empty by default (appsettings.json), and when unset it silently falls
-- back to the RLS-bound application role -- which under FORCE ROW LEVEL SECURITY (0002) means the
-- console would render a blank instance rather than failing loudly. These policies work on every
-- install, with or without a BYPASSRLS role.
--
-- Only two tables need one:
--   * tenancy.workspaces      -- bootstrap_workspace_read (0026) requires ACTIVE MEMBERSHIP in the
--                                workspace being read, so a host admin who is not a member reads zero
--                                rows. It also has no UPDATE policy at all (0029 left it with
--                                SELECT/INSERT only, 0092 added DELETE), so suspending a workspace
--                                (Workspace.Archive) is filtered to zero rows without host_admin_update.
--   * tenancy.workspace_members -- workspace_isolation (0029) is the strict "ambient workspace must
--                                match" form, and bootstrap_member_read (0020) only exposes the
--                                caller's OWN membership rows.
-- tenancy.feature_entitlements (feature_entitlement_isolation, 0001) and audit.audit_events
-- (audit_isolation, 0029) already use the lenient "ambient workspace unset -> all rows" form, so a
-- host request -- which deliberately carries no X-Workspace and therefore no app.current_workspace --
-- can already read them. No policy work needed there.
--
-- All policies are keyed on app.current_user, the same session variable the existing bootstrap_*
-- policies already trust (set per connection by WorkspaceConnectionInterceptor from the authenticated
-- principal, never from a request body or header). PERMISSIVE, so they OR with the existing policies
-- rather than narrowing them; no existing access changes. DROP ... IF EXISTS keeps the script
-- idempotent on an empty and an already-migrated database (AGENTS.md rule 9).

ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS is_host_admin boolean NOT NULL DEFAULT false;

-- Partial index: the only question ever asked of this column is "who are the host admins?" (the panel's
-- last-host-admin guard, and PlanvexaBootstrap's "does one exist yet?" check). Indexing only the true
-- rows keeps it to a handful of entries on any install.
CREATE INDEX IF NOT EXISTS ix_users_is_host_admin ON identity.users (is_host_admin) WHERE is_host_admin;

DROP POLICY IF EXISTS host_admin_read ON tenancy.workspaces;
CREATE POLICY host_admin_read ON tenancy.workspaces
FOR SELECT
USING (
    EXISTS (
        SELECT 1
        FROM identity.users u
        WHERE u.id = nullif(current_setting('app.current_user', true), '')::uuid
          AND u.is_host_admin
          AND u.is_active
    )
);

-- Suspend/restore (Workspace.Archive/Restore -> a status UPDATE). Scoped to host admins only: the
-- product has no owner-facing archive path, so this policy is the entire write surface for the column.
DROP POLICY IF EXISTS host_admin_update ON tenancy.workspaces;
CREATE POLICY host_admin_update ON tenancy.workspaces
FOR UPDATE
USING (
    EXISTS (
        SELECT 1
        FROM identity.users u
        WHERE u.id = nullif(current_setting('app.current_user', true), '')::uuid
          AND u.is_host_admin
          AND u.is_active
    )
)
WITH CHECK (
    EXISTS (
        SELECT 1
        FROM identity.users u
        WHERE u.id = nullif(current_setting('app.current_user', true), '')::uuid
          AND u.is_host_admin
          AND u.is_active
    )
);

DROP POLICY IF EXISTS host_admin_read ON tenancy.workspace_members;
CREATE POLICY host_admin_read ON tenancy.workspace_members
FOR SELECT
USING (
    EXISTS (
        SELECT 1
        FROM identity.users u
        WHERE u.id = nullif(current_setting('app.current_user', true), '')::uuid
          AND u.is_host_admin
          AND u.is_active
    )
);
