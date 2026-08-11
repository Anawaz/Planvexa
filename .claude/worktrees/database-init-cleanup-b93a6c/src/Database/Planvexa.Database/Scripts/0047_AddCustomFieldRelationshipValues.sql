-- Planvexa DbUp script 0047_AddCustomFieldRelationshipValues.sql
-- Custom fields completeness: Relationship field type.
--
-- work.custom_field_relationship_values: a Relationship-type custom field's directional link from one
-- task to another, keyed by field DEFINITION -- unlike the fixed, single work.task_relations
-- "relates to" edge, a workspace can define several differently-named relationship fields (e.g.
-- "Related Epic", "Blocked Deliverable") each with their own set of links. Directional (task_id ->
-- related_task_id only, no reverse row), workspace-scoped, not restricted to a common list.
--
-- Follows the standard workspace_id NOT NULL + sole workspace_isolation RLS pattern (see 0038's header).
-- IF NOT EXISTS guards: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS work.custom_field_relationship_values (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    definition_id uuid NOT NULL,
    task_id uuid NOT NULL,
    related_task_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_custom_field_relationship_values PRIMARY KEY (id),
    CONSTRAINT fk_cfrv_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_cfrv_custom_field_definitions_definition_id FOREIGN KEY (definition_id) REFERENCES work.custom_field_definitions (id) ON DELETE CASCADE,
    CONSTRAINT fk_cfrv_tasks_task_id FOREIGN KEY (task_id) REFERENCES work.tasks (id) ON DELETE CASCADE,
    CONSTRAINT fk_cfrv_tasks_related_task_id FOREIGN KEY (related_task_id) REFERENCES work.tasks (id) ON DELETE CASCADE,
    CONSTRAINT ck_cfrv_not_self CHECK (task_id <> related_task_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cfrv_definition_task_related ON work.custom_field_relationship_values (definition_id, task_id, related_task_id);
CREATE INDEX IF NOT EXISTS ix_cfrv_related_task_id ON work.custom_field_relationship_values (related_task_id);

ALTER TABLE work.custom_field_relationship_values ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.custom_field_relationship_values FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON work.custom_field_relationship_values;
CREATE POLICY workspace_isolation ON work.custom_field_relationship_values USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
