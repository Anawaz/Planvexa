-- Planvexa DbUp script 0045_AddCustomFieldUserTeamValues.sql
-- Custom fields completeness: User/Team field types.
--
-- work.custom_field_values.user_value / team_value: new typed projection columns alongside the existing
-- text_value/number_value/date_value/bool_value/option_id (ADR-0008). User references a workspace
-- member's user id (Identity is a global directory, not workspace-owned, so no FK -- same convention as
-- work.task_assignees.user_id). Team references a Tenancy Team id, deliberately unvalidated/opaque --
-- WorkManagement never reads Tenancy's own tables directly (AGENTS.md rule 7 module boundary), matching
-- work.task_team_assignees.team_id's existing convention.
--
-- DO-block column guards: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9).

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'custom_field_values' AND column_name = 'user_value'
    ) THEN
        ALTER TABLE work.custom_field_values ADD COLUMN user_value uuid NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'custom_field_values' AND column_name = 'team_value'
    ) THEN
        ALTER TABLE work.custom_field_values ADD COLUMN team_value uuid NULL;
    END IF;
END $$;
