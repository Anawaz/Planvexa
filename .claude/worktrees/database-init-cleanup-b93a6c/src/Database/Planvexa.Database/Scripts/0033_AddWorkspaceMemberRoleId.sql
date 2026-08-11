-- Planvexa DbUp script 0033_AddWorkspaceMemberRoleId.sql
-- ADR-0003: tenancy.workspace_members gets a nullable role_id pointing at its workspace's
-- tenancy.roles row. Null means "use the fast-path role column value" (see WorkspaceMember.RoleId doc
-- comment); non-null is the authorization source of truth going forward. Backfills every existing
-- membership to its matching built-in role before adding the FK -- 0032 (which runs first, DbUp
-- executes scripts in filename order) already seeded every existing workspace's built-in roles, so the
-- FK is satisfiable immediately. lower(m.role) matches a role key directly: the four pre-existing
-- MembershipRole values (Owner/Admin/Member/Guest) lowercase to exactly their role key; LimitedMember
-- did not exist before this change so no legacy row can carry it. Safe on an empty database (no
-- memberships -> no-op backfill) and on the current dev database (AGENTS.md rule 9).

ALTER TABLE tenancy.workspace_members ADD COLUMN IF NOT EXISTS role_id uuid;

UPDATE tenancy.workspace_members m
SET role_id = r.id
FROM tenancy.roles r
WHERE m.role_id IS NULL
  AND r.workspace_id = m.workspace_id
  AND r.key = lower(m.role);

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_workspace_members_roles_role_id') THEN
        ALTER TABLE tenancy.workspace_members
            ADD CONSTRAINT fk_workspace_members_roles_role_id FOREIGN KEY (role_id) REFERENCES tenancy.roles (id) ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_workspace_members_role_id ON tenancy.workspace_members (role_id);
