-- Planvexa DbUp script 0060_AddGoals.sql
-- Goals/OKRs, net new: a brand-new `goals` schema — goals.goal_folders (grouping),
-- goals.goals (the OKR/goal itself: numeric-target OR linked-tasks-ratio progress, see
-- Goal.TargetType), goals.goal_linked_tasks (join to WorkManagement tasks by id, no FK — modules
-- integrate through contracts/ids, never shared tables, AGENTS.md rule 7), goals.goal_comments (a
-- lightweight goal-scoped comment thread, see Goal's doc comment for why this isn't wired through
-- Collaboration). All CREATE ... IF NOT EXISTS: safe on both an empty database and the current
-- already-migrated dev database (AGENTS.md rule 9) -- every table here is brand new with no existing
-- rows to backfill. Every table gets its own workspace_id NOT NULL + sole workspace_isolation RLS
-- policy, the same pattern used by every workspace-owned table since 0029/0030 (see 0053/0059's header).

CREATE SCHEMA IF NOT EXISTS goals;

CREATE TABLE IF NOT EXISTS goals.goal_folders (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_goal_folders PRIMARY KEY (id),
    CONSTRAINT fk_goal_folders_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_goal_folders_workspace_id ON goals.goal_folders (workspace_id);

ALTER TABLE goals.goal_folders ENABLE ROW LEVEL SECURITY;
ALTER TABLE goals.goal_folders FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON goals.goal_folders;
CREATE POLICY workspace_isolation ON goals.goal_folders USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

CREATE TABLE IF NOT EXISTS goals.goals (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    folder_id uuid,
    name character varying(200) NOT NULL,
    description character varying(4000),
    owner_user_id uuid NOT NULL,
    start_date timestamp with time zone NOT NULL,
    end_date timestamp with time zone NOT NULL,
    target_type character varying(32) NOT NULL,
    target_value numeric(18,4),
    current_value numeric(18,4),
    status character varying(32) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_goals PRIMARY KEY (id),
    CONSTRAINT fk_goals_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_goals_goal_folders_folder_id FOREIGN KEY (folder_id) REFERENCES goals.goal_folders (id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_goals_workspace_id_folder_id ON goals.goals (workspace_id, folder_id);

ALTER TABLE goals.goals ENABLE ROW LEVEL SECURITY;
ALTER TABLE goals.goals FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON goals.goals;
CREATE POLICY workspace_isolation ON goals.goals USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

-- task_id references WorkManagement's work.work_items(id) by id only, no FK (AGENTS.md rule 7).
CREATE TABLE IF NOT EXISTS goals.goal_linked_tasks (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    goal_id uuid NOT NULL,
    task_id uuid NOT NULL,
    linked_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_goal_linked_tasks PRIMARY KEY (id),
    CONSTRAINT fk_goal_linked_tasks_goals_goal_id FOREIGN KEY (goal_id) REFERENCES goals.goals (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_goal_linked_tasks_goal_id_task_id ON goals.goal_linked_tasks (goal_id, task_id);
CREATE INDEX IF NOT EXISTS ix_goal_linked_tasks_workspace_id_task_id ON goals.goal_linked_tasks (workspace_id, task_id);

ALTER TABLE goals.goal_linked_tasks ENABLE ROW LEVEL SECURITY;
ALTER TABLE goals.goal_linked_tasks FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON goals.goal_linked_tasks;
CREATE POLICY workspace_isolation ON goals.goal_linked_tasks USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

CREATE TABLE IF NOT EXISTS goals.goal_comments (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    goal_id uuid NOT NULL,
    author_user_id uuid NOT NULL,
    body character varying(4000) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_goal_comments PRIMARY KEY (id),
    CONSTRAINT fk_goal_comments_goals_goal_id FOREIGN KEY (goal_id) REFERENCES goals.goals (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_goal_comments_workspace_id_goal_id_created_at_utc ON goals.goal_comments (workspace_id, goal_id, created_at_utc);

ALTER TABLE goals.goal_comments ENABLE ROW LEVEL SECURITY;
ALTER TABLE goals.goal_comments FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON goals.goal_comments;
CREATE POLICY workspace_isolation ON goals.goal_comments USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
