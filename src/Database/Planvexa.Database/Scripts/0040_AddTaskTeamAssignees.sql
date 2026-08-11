-- Planvexa DbUp script 0040_AddTaskTeamAssignees.sql
-- Task management completeness: team assignees.
--
-- work.task_team_assignees: a Team (tenancy.teams) assigned to a task, alongside the existing individual
-- work.task_assignees (user assignees). WorkManagement stores only team_id (an opaque id, same as
-- task_assignees.user_id) -- no FK to tenancy.teams, because a module must not reference another module's
-- tables directly (AGENTS.md rule 7); the Tenancy module remains the source of truth for whether a team id
-- is still valid, same as Identity remains the source of truth for user ids referenced by task_assignees.
--
-- Follows the standard workspace_id NOT NULL + sole workspace_isolation RLS pattern (see 0038's header).
-- IF NOT EXISTS guards: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS work.task_team_assignees (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    team_id uuid NOT NULL,
    assigned_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_task_team_assignees PRIMARY KEY (id),
    CONSTRAINT fk_task_team_assignees_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_task_team_assignees_tasks_task_id FOREIGN KEY (task_id) REFERENCES work.tasks (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_task_team_assignees_task_team ON work.task_team_assignees (task_id, team_id);
CREATE INDEX IF NOT EXISTS ix_task_team_assignees_team_id ON work.task_team_assignees (team_id);

ALTER TABLE work.task_team_assignees ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.task_team_assignees FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON work.task_team_assignees;
CREATE POLICY workspace_isolation ON work.task_team_assignees USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
