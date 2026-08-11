-- Planvexa DbUp script 0048_AddActivityFeedIndexAndTaskBaselines.sql
-- Views completion.
--
-- Two small additions, both additive/idempotent:
--
-- 1) work.task_activity_events: a new (workspace_id, created_at_utc DESC) index. The workspace-wide
--    Activity view (distinct from the existing per-task activity feed) pages the newest events across
--    every task in a workspace; the existing (workspace_id, task_id, created_at_utc) index (0030) is
--    keyed by task_id in the middle, so it cannot serve a plain workspace-wide "newest first" scan
--    without a sort. This index is additive only -- the 0030 index stays, it still serves the per-task
--    feed lookup.
--
-- 2) work.tasks: two nullable baseline columns (baseline_start_date, baseline_due_date) for the Gantt
--    view's baseline feature -- a snapshot of the originally-planned date range, captured explicitly by
--    a "Set Baseline" action and left untouched by ordinary reschedules, so the Gantt bar can show
--    "planned vs. current" drift. Both null until a baseline is first captured; no backfill needed.
--
-- IF NOT EXISTS / DO-block column guards: safe on both an empty database and the current
-- already-migrated dev database (AGENTS.md rule 9).

CREATE INDEX IF NOT EXISTS ix_task_activity_events_workspace_id_created_at_utc
    ON work.task_activity_events (workspace_id, created_at_utc DESC);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'tasks' AND column_name = 'baseline_start_date'
    ) THEN
        ALTER TABLE work.tasks ADD COLUMN baseline_start_date timestamp with time zone;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'tasks' AND column_name = 'baseline_due_date'
    ) THEN
        ALTER TABLE work.tasks ADD COLUMN baseline_due_date timestamp with time zone;
    END IF;
END $$;
