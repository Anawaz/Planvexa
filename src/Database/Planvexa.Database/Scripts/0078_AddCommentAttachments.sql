-- Planvexa DbUp script 0078_AddCommentAttachments.sql
-- Collaboration, net new table: collab.comment_attachments — file attachments on a Comment, same
-- shape/pipeline as work.task_attachments (0021/0030) but scoped to a comment_id instead of task_id.
-- A standalone table, not a child of collab.comments' own row (no cascade dependency modeling needed
-- beyond the FK below) — see CommentAttachment.cs's doc comment.
--
-- CREATE ... IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9) — this table is brand new with no existing rows to backfill. Gets its
-- own workspace_id NOT NULL + sole workspace_isolation RLS policy, the same pattern used by every
-- workspace-owned table since 0029/0030 (see 0076's header for the identical shape).

CREATE TABLE IF NOT EXISTS collab.comment_attachments (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    comment_id uuid NOT NULL,
    file_name character varying(260) NOT NULL,
    content_type character varying(200) NOT NULL,
    size_bytes bigint NOT NULL,
    storage_path character varying(500) NOT NULL,
    uploaded_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_comment_attachments PRIMARY KEY (id),
    CONSTRAINT fk_comment_attachments_comments_comment_id FOREIGN KEY (comment_id) REFERENCES collab.comments (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_comment_attachments_workspace_id_comment_id ON collab.comment_attachments (workspace_id, comment_id);

ALTER TABLE collab.comment_attachments ENABLE ROW LEVEL SECURITY;
ALTER TABLE collab.comment_attachments FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON collab.comment_attachments;
CREATE POLICY workspace_isolation ON collab.comment_attachments USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
