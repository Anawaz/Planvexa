-- Planvexa DbUp script 0079_AddFormsTargetUserId.sql
-- Forms full routing: adds a target_user_id column next to the existing target_team_id (0058), so a
-- form's settings can also assign the created task to a specific workspace member, not just a team.
-- Opaque cross-module id, unvalidated here (same pattern as target_team_id) — PublicFormService calls
-- ITaskWriteApi.AssignAsync, which itself no-ops if the user isn't a member of the task's workspace.
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9) — nullable, no backfill needed for existing rows.

ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS target_user_id uuid;
