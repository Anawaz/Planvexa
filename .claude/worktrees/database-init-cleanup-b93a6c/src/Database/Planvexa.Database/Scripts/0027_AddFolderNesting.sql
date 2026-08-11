-- Folder nesting (one level of subfolders).
--
-- Adds an optional self-reference so a Folder can sit under another Folder in the same Space. The
-- column is nullable, so every existing folder stays top-level with no backfill. The application
-- enforces a single level of nesting; the schema itself is ready for deeper nesting later. The
-- composite FK keeps a subfolder in the same tenant as its parent and cascades on delete.

ALTER TABLE work.folders ADD COLUMN parent_folder_id uuid;

ALTER TABLE work.folders
    ADD CONSTRAINT fk_folders_parent_tenant_id_parent_folder_id
    FOREIGN KEY (tenant_id, parent_folder_id)
    REFERENCES work.folders (tenant_id, id) ON DELETE CASCADE;

CREATE INDEX ix_folders_tenant_id_parent_folder_id ON work.folders (tenant_id, parent_folder_id);
