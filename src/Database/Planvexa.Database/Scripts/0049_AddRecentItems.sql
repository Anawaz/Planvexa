-- Planvexa DbUp script 0049_AddRecentItems.sql
-- Search and navigation completeness: recent items.
--
-- work.recent_items: a user's most-recent view of a resource, across any resource kind. Free-form
-- resource_type mirrors work.work_favorites' convention (see 0038's header) so later resource kinds
-- (Document, Dashboard, ChatChannel, Form, ...) need no schema change. One row per
-- (workspace_id, user_id, resource_type, resource_id): the application upserts on repeat views (bumps
-- viewed_at_utc) rather than inserting a duplicate, and caps the row count per user by deleting the
-- oldest overflow after an insert (RecentItemService, not enforced in SQL).
--
-- Same workspace_id NOT NULL + sole workspace_isolation RLS policy pattern used by every workspace-owned
-- table since 0029/0030 (see 0034's header). IF NOT EXISTS / CREATE OR REPLACE guards throughout: safe on
-- both an empty database and the current already-migrated dev database (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS work.recent_items (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    resource_type character varying(32) NOT NULL,
    resource_id uuid NOT NULL,
    viewed_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_recent_items PRIMARY KEY (id),
    CONSTRAINT fk_recent_items_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_recent_items_workspace_user_resource
    ON work.recent_items (workspace_id, user_id, resource_type, resource_id);

-- Serves "most-recent N for this user", the only read pattern RecentItemService uses.
CREATE INDEX IF NOT EXISTS ix_recent_items_workspace_user_viewed_at_utc
    ON work.recent_items (workspace_id, user_id, viewed_at_utc DESC);

ALTER TABLE work.recent_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.recent_items FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON work.recent_items;
CREATE POLICY workspace_isolation ON work.recent_items USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
