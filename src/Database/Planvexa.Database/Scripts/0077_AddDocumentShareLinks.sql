-- Planvexa DbUp script 0077_AddDocumentShareLinks.sql
-- Documents, net new table: docs.document_share_links — public, view-only share links for documents,
-- same shape as sharing.share_links (tasks, see 0005/0036/0051) but scoped to docs and without the
-- permission-level/guest-comment columns (public document sharing is view-only; see
-- DocumentShareLink.cs's doc comment for why this is a Documents-module duplicate rather than a
-- cross-module reference).
--
-- CREATE ... IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9) — this table is brand new with no existing rows to backfill.
--
-- Unlike docs.document_comments, this table needs a maintenance-connection cross-workspace lookup by
-- token hash for the anonymous read path (same reason as sharing.share_links), so its RLS policy is
-- the plain workspace_isolation shape rather than anything token-aware — the maintenance connection
-- bypasses RLS entirely for that one lookup (see MaintenanceConnection.cs).

CREATE TABLE IF NOT EXISTS docs.document_share_links (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    document_id uuid NOT NULL,
    token_hash character varying(128) NOT NULL,
    password_hash character varying(256),
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    expires_at_utc timestamp with time zone,
    is_revoked boolean NOT NULL,
    CONSTRAINT pk_document_share_links PRIMARY KEY (id),
    CONSTRAINT fk_document_share_links_documents_document_id FOREIGN KEY (document_id) REFERENCES docs.documents (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_document_share_links_token_hash ON docs.document_share_links (token_hash);
CREATE INDEX IF NOT EXISTS ix_document_share_links_workspace_id_document_id ON docs.document_share_links (workspace_id, document_id);

ALTER TABLE docs.document_share_links ENABLE ROW LEVEL SECURITY;
ALTER TABLE docs.document_share_links FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON docs.document_share_links;
CREATE POLICY workspace_isolation ON docs.document_share_links USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
