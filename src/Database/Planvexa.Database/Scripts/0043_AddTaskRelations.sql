-- Planvexa DbUp script 0043_AddTaskRelations.sql
-- Task management completeness: generic/free-form task relationships.
--
-- work.task_relations: a free-form, symmetric "relates to" link between two tasks -- no scheduling
-- semantics, alongside the existing typed work.task_dependencies (Blocks/BlockedBy/WaitingOn). One row
-- per pair; the application queries both task_id and related_task_id so either side of the pair finds it
-- (see ITaskRelationStore), so no canonical (task_id < related_task_id) ordering is enforced here.
--
-- Follows the standard workspace_id NOT NULL + sole workspace_isolation RLS pattern (see 0038's header).
-- IF NOT EXISTS guards: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS work.task_relations (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    related_task_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_task_relations PRIMARY KEY (id),
    CONSTRAINT fk_task_relations_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_task_relations_tasks_task_id FOREIGN KEY (task_id) REFERENCES work.tasks (id) ON DELETE CASCADE,
    CONSTRAINT fk_task_relations_tasks_related_task_id FOREIGN KEY (related_task_id) REFERENCES work.tasks (id) ON DELETE CASCADE,
    CONSTRAINT ck_task_relations_not_self CHECK (task_id <> related_task_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_task_relations_task_related ON work.task_relations (task_id, related_task_id);
CREATE INDEX IF NOT EXISTS ix_task_relations_related_task_id ON work.task_relations (related_task_id);

ALTER TABLE work.task_relations ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.task_relations FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON work.task_relations;
CREATE POLICY workspace_isolation ON work.task_relations USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
