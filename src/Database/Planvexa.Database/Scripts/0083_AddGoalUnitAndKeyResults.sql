-- Planvexa DbUp script 0083_AddGoalUnitAndKeyResults.sql
-- Goals gained: (1) a display-only `unit` column (Number/Currency/Percent) for formatting a
-- Numeric-target goal's current/target values, purely cosmetic on top of the existing decimal math; and
-- (2) goals.goal_key_results, an owned OKR-style key-result child entity (mirrors goal_linked_tasks'
-- shape/RLS pattern) so a single Goal can track multiple weighted key results whose average completion
-- becomes the goal's overall progress once any exist (see GoalProgressCalculator).
--
-- ADD COLUMN IF NOT EXISTS / CREATE TABLE IF NOT EXISTS: safe on both an empty database and the current
-- already-migrated dev database (AGENTS.md rule 9). `unit` gets a DEFAULT so existing goal rows backfill
-- to 'Number' without a separate UPDATE.

ALTER TABLE goals.goals ADD COLUMN IF NOT EXISTS unit character varying(32) NOT NULL DEFAULT 'Number';

CREATE TABLE IF NOT EXISTS goals.goal_key_results (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    goal_id uuid NOT NULL,
    title character varying(200) NOT NULL,
    current_value numeric(18,4) NOT NULL,
    target_value numeric(18,4) NOT NULL,
    unit character varying(32) NOT NULL DEFAULT 'Number',
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_goal_key_results PRIMARY KEY (id),
    CONSTRAINT fk_goal_key_results_goals_goal_id FOREIGN KEY (goal_id) REFERENCES goals.goals (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_goal_key_results_workspace_id_goal_id ON goals.goal_key_results (workspace_id, goal_id);

ALTER TABLE goals.goal_key_results ENABLE ROW LEVEL SECURITY;
ALTER TABLE goals.goal_key_results FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON goals.goal_key_results;
CREATE POLICY workspace_isolation ON goals.goal_key_results USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
