-- Planvexa DbUp script 0081_AddSprintGoal.sql
-- Sprints gained a free-text Goal (nullable) so a sprint can carry an iteration objective.
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9) -- nullable, no backfill needed for existing rows.

ALTER TABLE planning.sprints ADD COLUMN IF NOT EXISTS goal character varying(2000);
