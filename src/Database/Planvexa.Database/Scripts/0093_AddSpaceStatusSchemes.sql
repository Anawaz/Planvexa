-- Planvexa DbUp script 0093_AddSpaceStatusSchemes.sql
-- Workspace-default status schemes with optional per-Space overrides.
--
-- work.status_schemes.space_id: NULL = a workspace-level scheme (the workspace default is always one of
-- these -- IStatusSchemeStore.FindDefaultAsync filters on space_id IS NULL so a Space override can never
-- be mistaken for it); set = a scheme owned by that Space. ON DELETE CASCADE, because a Space's override
-- has no meaning once the Space is gone.
--
-- work.spaces.status_scheme_id: NULL = inherit the workspace default; set = this Space's override, which
-- TaskListService.CreateAsync uses as the fallback scheme for new lists in that Space. ON DELETE SET NULL
-- so deleting the scheme reverts the Space to inheriting rather than dangling.
--
-- Both columns are nullable and default NULL, so on an upgraded database every existing Space inherits and
-- every existing list keeps the scheme it already points at -- no behaviour changes until a Space opts in.
-- ADD COLUMN IF NOT EXISTS + guarded constraint adds: safe on an empty and an already-migrated database
-- (AGENTS.md rule 9). Both tables are already in the workspace_isolation RLS set, so no policy work here.

ALTER TABLE work.status_schemes ADD COLUMN IF NOT EXISTS space_id uuid;
ALTER TABLE work.spaces ADD COLUMN IF NOT EXISTS status_scheme_id uuid;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_status_schemes_space') THEN
        ALTER TABLE work.status_schemes
            ADD CONSTRAINT fk_status_schemes_space
            FOREIGN KEY (space_id) REFERENCES work.spaces (id) ON DELETE CASCADE;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_spaces_status_scheme') THEN
        ALTER TABLE work.spaces
            ADD CONSTRAINT fk_spaces_status_scheme
            FOREIGN KEY (status_scheme_id) REFERENCES work.status_schemes (id) ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_status_schemes_workspace_space
    ON work.status_schemes (workspace_id, space_id);
