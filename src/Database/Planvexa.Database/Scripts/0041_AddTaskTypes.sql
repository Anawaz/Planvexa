-- Planvexa DbUp script 0041_AddTaskTypes.sql
-- Task management completeness: custom task types.
--
-- work.task_types: a workspace-configurable task type (e.g. "Task", "Bug", "Milestone"), same shape as
-- work.status_schemes' custom statuses. work.tasks.task_type_id is nullable -- null means "the workspace's
-- built-in default type" so no backfill/default assignment is required for existing tasks; the built-in
-- type itself is seeded lazily by the application the first time task types are read for a workspace
-- (WorkspaceProvisioningService, mirroring how the default StatusScheme is seeded), not by this script.
--
-- Follows the standard workspace_id NOT NULL + sole workspace_isolation RLS pattern (see 0038's header).
-- IF NOT EXISTS / DO-block column guards: safe on both an empty database and the current already-migrated
-- dev database (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS work.task_types (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    color character varying(32) NOT NULL DEFAULT '#8b8b8b',
    icon character varying(64),
    is_built_in boolean NOT NULL DEFAULT false,
    position double precision NOT NULL DEFAULT 0,
    CONSTRAINT pk_task_types PRIMARY KEY (id),
    CONSTRAINT fk_task_types_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_task_types_workspace_name ON work.task_types (workspace_id, name);

ALTER TABLE work.task_types ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.task_types FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON work.task_types;
CREATE POLICY workspace_isolation ON work.task_types USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'tasks' AND column_name = 'task_type_id'
    ) THEN
        ALTER TABLE work.tasks ADD COLUMN task_type_id uuid NULL;
        ALTER TABLE work.tasks ADD CONSTRAINT fk_tasks_task_types_task_type_id
            FOREIGN KEY (task_type_id) REFERENCES work.task_types (id) ON DELETE SET NULL;
        CREATE INDEX ix_tasks_task_type_id ON work.tasks (task_type_id);
    END IF;
END $$;
