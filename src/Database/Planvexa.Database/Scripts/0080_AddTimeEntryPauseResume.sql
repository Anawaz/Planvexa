-- Planvexa DbUp script 0080_AddTimeEntryPauseResume.sql
-- Time tracking: pause/resume for a running timer (TimeEntry.Pause/Resume). paused_at_utc is set
-- while paused and null otherwise; paused_seconds accumulates completed pause intervals and is
-- folded in on Resume/Stop so duration stays server-authoritative (see TimeEntry.cs's doc comment).
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9) -- paused_seconds defaults to 0 for existing rows (never paused).

ALTER TABLE time.time_entries ADD COLUMN IF NOT EXISTS paused_at_utc timestamp with time zone;
ALTER TABLE time.time_entries ADD COLUMN IF NOT EXISTS paused_seconds bigint NOT NULL DEFAULT 0;
