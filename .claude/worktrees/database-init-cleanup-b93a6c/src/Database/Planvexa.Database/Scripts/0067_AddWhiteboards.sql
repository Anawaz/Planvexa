-- Planvexa DbUp script 0067_AddWhiteboards.sql
-- Whiteboards, net new: a brand-new `whiteboards` schema — whiteboards.whiteboards (metadata:
-- name/privacy/owner/optional Task-or-Document link, see Whiteboard.cs's doc comment), whiteboards.whiteboard_templates
-- (reusable content snapshots, seed_state is a raw copy of a whiteboard_collab_state row's y_state),
-- whiteboards.whiteboard_collab_state (Yjs CRDT binary state for shapes/connectors/sticky-notes/text/images,
-- owned by apps/collaboration's Hocuspocus server — exact same shape/purpose as docs.document_collab_state
-- from 0057_AddDocumentCollabState.sql; see that script's header for the "why Postgres, why a resumable
-- working buffer not durable history" rationale, unchanged here).
--
-- All CREATE ... IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9) — every table here is brand new with no existing rows to backfill. Every
-- workspace-owned table gets its own workspace_id NOT NULL + sole workspace_isolation RLS policy, the same
-- pattern used by every workspace-owned table since 0029/0030 (see 0060's header for goals' identical shape).

CREATE SCHEMA IF NOT EXISTS whiteboards;

CREATE TABLE IF NOT EXISTS whiteboards.whiteboards (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    is_private boolean NOT NULL,
    owner_user_id uuid NOT NULL,
    linked_resource_type character varying(32),
    linked_resource_id uuid,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    archived_at_utc timestamp with time zone,
    CONSTRAINT pk_whiteboards PRIMARY KEY (id),
    CONSTRAINT fk_whiteboards_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_whiteboards_workspace_id ON whiteboards.whiteboards (workspace_id);
CREATE INDEX IF NOT EXISTS ix_whiteboards_linked_resource ON whiteboards.whiteboards (linked_resource_type, linked_resource_id);

ALTER TABLE whiteboards.whiteboards ENABLE ROW LEVEL SECURITY;
ALTER TABLE whiteboards.whiteboards FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON whiteboards.whiteboards;
CREATE POLICY workspace_isolation ON whiteboards.whiteboards USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

CREATE TABLE IF NOT EXISTS whiteboards.whiteboard_templates (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    seed_state bytea,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_whiteboard_templates PRIMARY KEY (id),
    CONSTRAINT fk_whiteboard_templates_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_whiteboard_templates_workspace_id ON whiteboards.whiteboard_templates (workspace_id);

ALTER TABLE whiteboards.whiteboard_templates ENABLE ROW LEVEL SECURITY;
ALTER TABLE whiteboards.whiteboard_templates FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON whiteboards.whiteboard_templates;
CREATE POLICY workspace_isolation ON whiteboards.whiteboard_templates USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

-- Yjs collaboration state (apps/collaboration reads/writes this directly, mirrors docs.document_collab_state).
CREATE TABLE IF NOT EXISTS whiteboards.whiteboard_collab_state (
    whiteboard_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    y_state bytea NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_whiteboard_collab_state PRIMARY KEY (whiteboard_id),
    CONSTRAINT fk_whiteboard_collab_state_whiteboards FOREIGN KEY (whiteboard_id) REFERENCES whiteboards.whiteboards (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_whiteboard_collab_state_workspace_id ON whiteboards.whiteboard_collab_state (workspace_id);

ALTER TABLE whiteboards.whiteboard_collab_state ENABLE ROW LEVEL SECURITY;
ALTER TABLE whiteboards.whiteboard_collab_state FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON whiteboards.whiteboard_collab_state;
CREATE POLICY workspace_isolation ON whiteboards.whiteboard_collab_state USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
