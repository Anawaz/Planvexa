-- Planvexa DbUp script 0076_AddDocumentComments.sql
-- Documents, net new table: docs.document_comments — a lightweight dedicated comment thread on a
-- Document, same design choice and shape as clips.clip_comments from 0068_AddClips.sql (itself modeled on
-- goals.goal_comments from 0060_AddGoals.sql) — see DocumentComment.cs's doc comment for why.
--
-- CREATE ... IF NOT EXISTS: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9) — this table is brand new with no existing rows to backfill. Gets its own
-- workspace_id NOT NULL + sole workspace_isolation RLS policy, the same pattern used by every
-- workspace-owned table since 0029/0030 (see 0068's header for the identical shape).

CREATE TABLE IF NOT EXISTS docs.document_comments (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    document_id uuid NOT NULL,
    author_user_id uuid NOT NULL,
    body character varying(4000) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_document_comments PRIMARY KEY (id),
    CONSTRAINT fk_document_comments_documents_document_id FOREIGN KEY (document_id) REFERENCES docs.documents (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_document_comments_workspace_id_document_id_created_at_utc ON docs.document_comments (workspace_id, document_id, created_at_utc);

ALTER TABLE docs.document_comments ENABLE ROW LEVEL SECURITY;
ALTER TABLE docs.document_comments FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON docs.document_comments;
CREATE POLICY workspace_isolation ON docs.document_comments USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
