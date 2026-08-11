-- Planvexa DbUp script 0075_AddMyWorkPreferences.sql
-- My Work personal sort/organize preferences (product spec section 15: "Users should be able to filter
-- and organize My Work without modifying the underlying shared structure").
--
-- work.my_work_preferences: ONE global row per user, deliberately WITHOUT workspace_id and WITHOUT Row
-- Level Security. Every other per-user table in this schema (work_favorites, recent_items) is
-- workspace_id-scoped with a workspace_isolation RLS policy, but My Work spans every Workspace the user
-- belongs to (WorkItemService.ListMineAsync's optional workspaceId) rather than one — so this preference
-- cannot be pinned to a single Workspace. This is AGENTS.md rule 4's "truly global user preferences"
-- exception, the same shape as identity.users: no RLS, isolation enforced by the application layer always
-- scoping reads/writes to ICurrentUser.UserId (MyWorkPreferenceService), never a caller-supplied id.
--
-- IF NOT EXISTS / CREATE OR REPLACE guards: safe on both an empty database and the current
-- already-migrated dev database (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS work.my_work_preferences (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    sort_by character varying(32) NOT NULL,
    hidden_sections jsonb NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_my_work_preferences PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_my_work_preferences_user_id ON work.my_work_preferences (user_id);
