-- Planvexa DbUp script 0032_SeedBuiltInRolesForExistingWorkspaces.sql
-- ADR-0003: backfills the five built-in roles into tenancy.roles/tenancy.role_permissions for
-- every workspace that already existed before 0031 added the tables. New workspaces get the same rows
-- seeded in-process by WorkspaceRegistrationService (src/Modules/Tenancy/.../Application/BuiltInRoles.cs
-- is the C# source of truth -- keep the VALUES below in sync with it if it ever changes; this script is
-- immutable once shipped, so a later drift needs a follow-up backfill script, not an edit here), so
-- this script only needs to catch up existing data. NOT EXISTS guards make both INSERTs idempotent --
-- safe to rerun, and safe on an empty database (no workspaces yet -> the CROSS JOIN below produces zero
-- rows -> no-op) per AGENTS.md rule 9.

INSERT INTO tenancy.roles (id, workspace_id, key, name, is_built_in, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), w.id, def.key, def.name, true, now(), now()
FROM tenancy.workspaces w
CROSS JOIN (VALUES
    ('owner', 'Owner'),
    ('admin', 'Admin'),
    ('member', 'Member'),
    ('limited_member', 'Limited Member'),
    ('guest', 'Guest')
) AS def(key, name)
WHERE NOT EXISTS (
    SELECT 1 FROM tenancy.roles r WHERE r.workspace_id = w.id AND r.key = def.key
);

INSERT INTO tenancy.role_permissions (role_id, workspace_id, permission_key)
SELECT r.id, r.workspace_id, grant_.permission_key
FROM tenancy.roles r
JOIN (VALUES
    ('owner', 'workspace.manage'), ('owner', 'members.view'), ('owner', 'members.invite'),
    ('owner', 'members.manage'), ('owner', 'roles.manage'), ('owner', 'features.view'),
    ('owner', 'space.view'), ('owner', 'space.edit'), ('owner', 'space.manage'),
    ('owner', 'task.view'), ('owner', 'task.comment'), ('owner', 'task.edit'),
    ('owner', 'task.manage'), ('owner', 'task.share'),

    ('admin', 'workspace.manage'), ('admin', 'members.view'), ('admin', 'members.invite'),
    ('admin', 'members.manage'), ('admin', 'features.view'),
    ('admin', 'space.view'), ('admin', 'space.edit'), ('admin', 'space.manage'),
    ('admin', 'task.view'), ('admin', 'task.comment'), ('admin', 'task.edit'),
    ('admin', 'task.manage'), ('admin', 'task.share'),

    ('member', 'members.view'), ('member', 'features.view'),
    ('member', 'space.view'), ('member', 'space.edit'),
    ('member', 'task.view'), ('member', 'task.comment'), ('member', 'task.edit'),

    ('limited_member', 'features.view'),
    ('limited_member', 'task.view'), ('limited_member', 'task.comment'), ('limited_member', 'task.edit'),

    ('guest', 'task.view')
) AS grant_(role_key, permission_key) ON grant_.role_key = r.key
WHERE NOT EXISTS (
    SELECT 1 FROM tenancy.role_permissions rp WHERE rp.role_id = r.id AND rp.permission_key = grant_.permission_key
);
