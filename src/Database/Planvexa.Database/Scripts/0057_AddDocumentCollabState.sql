-- Planvexa DbUp script 0057_AddDocumentCollabState.sql
-- Documents & Wikis, item 3: Yjs document state persistence for the apps/collaboration
-- Hocuspocus server. One row per document holding the latest merged Yjs binary update (a Y.Doc encoded via
-- Y.encodeStateAsUpdate); apps/collaboration reads it via @hocuspocus/extension-database's `fetch` hook to
-- seed a room and writes it back via the `store` hook on a debounced interval and on last-editor-leaves.
-- This is infrastructure the Node collaboration host owns and writes to directly (no infra beyond
-- PostgreSQL, which is already provisioned -- see docker-compose.yml/AGENTS.md; nothing else suitable
-- (Redis) is in the dev stack, so this table is the periodic-flush persistence layer). The .NET DocumentVersion
-- table remains the durable, human-browsable version history; this table is a resumable working buffer only.
--
-- Idempotent: no-op if re-run against an already-migrated or empty database, per AGENTS.md rule 9.

CREATE TABLE IF NOT EXISTS docs.document_collab_state (
    document_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    y_state bytea NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_document_collab_state PRIMARY KEY (document_id),
    CONSTRAINT fk_document_collab_state_documents FOREIGN KEY (document_id) REFERENCES docs.documents (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_document_collab_state_workspace_id ON docs.document_collab_state (workspace_id);
