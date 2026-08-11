-- Planvexa DbUp script 0055_AddDocumentHierarchyAndTemplates.sql
-- Documents & Wikis, item 5+6: self-referencing parent_document_id so documents can nest into a
-- wiki tree independent of their Space/List/Task association (same cycle-prevention discipline as the -- Folder nesting -- enforced in the application layer via DocumentHierarchy.CreatesCycle, not the DB), and
-- a document_templates table (item 6).
--
-- Idempotent: IF NOT EXISTS / ADD COLUMN IF NOT EXISTS guards make this safe to re-run against an
-- already-migrated database, and a no-op against an empty database, per AGENTS.md rule 9.

ALTER TABLE docs.documents ADD COLUMN IF NOT EXISTS parent_document_id uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_schema = 'docs' AND table_name = 'documents' AND constraint_name = 'fk_documents_parent_document_id'
    ) THEN
        ALTER TABLE docs.documents
            ADD CONSTRAINT fk_documents_parent_document_id
            FOREIGN KEY (parent_document_id) REFERENCES docs.documents (id) ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_documents_parent_document_id ON docs.documents (parent_document_id);

CREATE TABLE IF NOT EXISTS docs.document_templates (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    content text NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_document_templates PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_document_templates_workspace_id ON docs.document_templates (workspace_id);
