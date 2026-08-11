-- Planvexa DbUp script 0042_AddTaskCustomIdAndEstimate.sql
-- Task management completeness: user-defined custom task id.
--
-- work.tasks.custom_id: an optional user-settable id/key, distinct from the auto Sequence, unique per
-- List (not workspace-wide -- two different Lists commonly reuse short keys like "1", "BUG-1" in
-- task tools generally; per-list scoping matches Sequence's own per-list numbering, see TaskList's
-- NextTaskSequence()). NULL is unconstrained (most tasks will never set one).
--
-- Estimates are NOT in this script: Planning.TaskEstimate (planning.task_estimates,
-- GET/PUT /api/v1/tasks/{taskId}/estimate) already exists as a real, working per-task estimate concept,
-- already wired into Reporting's EstimateVsActual widget -- adding a second estimate column on work.tasks
-- would just duplicate it (see WorkItem.cs's doc comment for detail).
--
-- DO-block column guard: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9).

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'tasks' AND column_name = 'custom_id'
    ) THEN
        ALTER TABLE work.tasks ADD COLUMN custom_id character varying(64) NULL;
        CREATE UNIQUE INDEX ux_tasks_list_id_custom_id ON work.tasks (list_id, custom_id) WHERE custom_id IS NOT NULL;
    END IF;
END $$;
