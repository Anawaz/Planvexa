-- Planvexa DbUp script 0039_AddTaskListMemberships.sql
-- Task management completeness: multi-list task membership.
--
-- work.task_list_memberships: a task's many-to-many membership in Lists, replacing the old
-- one-list-only work.tasks.list_id FK as the source of truth for "which lists is this task in". Exactly
-- one row per task has is_primary = true; work.tasks.list_id/space_id are KEPT (not dropped) as a
-- denormalized pointer to that primary membership's list, because most existing call sites (status-scheme
-- resolution, breadcrumbs, search, "my tasks", direct-by-id privacy resolution) mean "the task's one true
-- list" and were never written to enumerate multiple memberships. See WorkItem.cs and
-- WorkResourceHierarchyQuery.cs doc comments for the full design rationale.
--
-- Backfill: one is_primary = true membership row per existing task, mirroring its current list_id/position
-- exactly, so every task that existed before this script keeps behaving exactly as before (single list,
-- same order) until the application starts adding a task to a second list.
--
-- Follows the exact workspace_id NOT NULL + sole workspace_isolation RLS policy pattern used by every
-- workspace-owned table since 0029/0030 (see 0038's header). IF NOT EXISTS / NOT EXISTS guards throughout:
-- safe on both an empty database and the current already-migrated dev database (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS work.task_list_memberships (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    list_id uuid NOT NULL,
    is_primary boolean NOT NULL DEFAULT false,
    position double precision NOT NULL DEFAULT 0,
    added_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_task_list_memberships PRIMARY KEY (id),
    CONSTRAINT fk_task_list_memberships_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_task_list_memberships_tasks_task_id FOREIGN KEY (task_id) REFERENCES work.tasks (id) ON DELETE CASCADE,
    CONSTRAINT fk_task_list_memberships_lists_list_id FOREIGN KEY (list_id) REFERENCES work.lists (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_task_list_memberships_task_list ON work.task_list_memberships (task_id, list_id);
CREATE INDEX IF NOT EXISTS ix_task_list_memberships_list_position ON work.task_list_memberships (list_id, position);
CREATE INDEX IF NOT EXISTS ix_task_list_memberships_workspace_id ON work.task_list_memberships (workspace_id);

ALTER TABLE work.task_list_memberships ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.task_list_memberships FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON work.task_list_memberships;
CREATE POLICY workspace_isolation ON work.task_list_memberships USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

-- Backfill: every existing task gets exactly one primary membership mirroring its current list/position.
INSERT INTO work.task_list_memberships (id, workspace_id, task_id, list_id, is_primary, position, added_at_utc)
SELECT gen_random_uuid(), t.workspace_id, t.id, t.list_id, true, t.position, t.created_at_utc
FROM work.tasks t
WHERE NOT EXISTS (
    SELECT 1 FROM work.task_list_memberships m WHERE m.task_id = t.id
);
