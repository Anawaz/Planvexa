-- Planvexa DbUp script 0037_AddFolderParentForeignKeyAndDefaultViews.sql
-- Work hierarchy completeness.
--
-- Folders now nest to arbitrary depth (the application-level "one level only" restriction in
-- FolderService.CreateAsync is removed; cycle prevention on re-parenting is enforced in the domain/
-- service layer via FolderHierarchy.CreatesCycle, not the database). The schema already supports
-- arbitrary depth via the nullable self-referencing parent_folder_id column added by 0027 — but that
-- column has had NO foreign key since 0030 dropped the old composite (tenant_id, parent_folder_id) FK
-- via CASCADE and never added a plain replacement (0030's own replacement-FK step covers every other
-- child table but missed this one). Add the plain self-referencing FK now so referential integrity is
-- actually enforced, matching HierarchyConfigurations.FolderConfiguration's
-- `HasOne<Folder>().WithMany().HasForeignKey(x => x.ParentFolderId).OnDelete(DeleteBehavior.Restrict)`.
--
-- Default_view_id lets a Space/Folder/List record which SavedView opens by default; nullable
-- (null = "no default set, fall back to the first view" — today's behavior, unchanged). ON DELETE SET
-- NULL so deleting the referenced view un-sets the default instead of blocking the delete.
--
-- IF NOT EXISTS / ADD COLUMN IF NOT EXISTS throughout: safe on both an empty database and the current
-- already-migrated dev database (AGENTS.md rule 9).

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_folders_parent_folder_id') THEN
        ALTER TABLE work.folders
            ADD CONSTRAINT fk_folders_parent_folder_id
            FOREIGN KEY (parent_folder_id) REFERENCES work.folders (id) ON DELETE RESTRICT;
    END IF;
END $$;

ALTER TABLE work.spaces ADD COLUMN IF NOT EXISTS default_view_id uuid;
ALTER TABLE work.folders ADD COLUMN IF NOT EXISTS default_view_id uuid;
ALTER TABLE work.lists ADD COLUMN IF NOT EXISTS default_view_id uuid;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_spaces_saved_views_default_view_id') THEN
        ALTER TABLE work.spaces
            ADD CONSTRAINT fk_spaces_saved_views_default_view_id
            FOREIGN KEY (default_view_id) REFERENCES work.saved_views (id) ON DELETE SET NULL;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_folders_saved_views_default_view_id') THEN
        ALTER TABLE work.folders
            ADD CONSTRAINT fk_folders_saved_views_default_view_id
            FOREIGN KEY (default_view_id) REFERENCES work.saved_views (id) ON DELETE SET NULL;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_lists_saved_views_default_view_id') THEN
        ALTER TABLE work.lists
            ADD CONSTRAINT fk_lists_saved_views_default_view_id
            FOREIGN KEY (default_view_id) REFERENCES work.saved_views (id) ON DELETE SET NULL;
    END IF;
END $$;
