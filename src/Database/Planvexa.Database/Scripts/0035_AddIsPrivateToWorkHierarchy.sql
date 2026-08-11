-- Planvexa DbUp script 0035_AddIsPrivateToWorkHierarchy.sql
-- ADR-0003: adds is_private (default false = today's workspace-wide-per-role visibility) to
-- the four ACL resource types owned by WorkManagement: spaces, folders, lists, tasks. ADD COLUMN IF NOT
-- EXISTS with a DEFAULT is safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9) -- existing rows backfill to false via the column default, no separate UPDATE needed.

ALTER TABLE work.spaces ADD COLUMN IF NOT EXISTS is_private boolean NOT NULL DEFAULT false;
ALTER TABLE work.folders ADD COLUMN IF NOT EXISTS is_private boolean NOT NULL DEFAULT false;
ALTER TABLE work.lists ADD COLUMN IF NOT EXISTS is_private boolean NOT NULL DEFAULT false;
ALTER TABLE work.tasks ADD COLUMN IF NOT EXISTS is_private boolean NOT NULL DEFAULT false;
