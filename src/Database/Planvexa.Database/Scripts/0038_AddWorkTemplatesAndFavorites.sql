-- Planvexa DbUp script 0038_AddWorkTemplatesAndFavorites.sql
-- Work hierarchy completeness: templates + favourites.
--
-- work.work_templates: a reusable structural snapshot of a Space/Folder/List (sub-structure, status
-- scheme, custom-field definitions — never task instances/content), captured as opaque JSON the
-- application owns the shape of (WorkTemplateService), same pattern as work.saved_views.config_json.
--
-- work.work_favorites: a user's bookmark of a work resource. resource_type is free-form (mirrors
-- tenancy.resource_permissions) so later resource kinds (Task, View, ...) need no schema change.
--
-- Both follow the exact workspace_id NOT NULL + sole workspace_isolation RLS policy pattern used by
-- every workspace-owned table since 0029/0030 (see 0034's header). IF NOT EXISTS / CREATE OR REPLACE
-- guards throughout: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS work.work_templates (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    resource_type character varying(16) NOT NULL,
    name character varying(200) NOT NULL,
    structure_json jsonb NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_work_templates PRIMARY KEY (id),
    CONSTRAINT fk_work_templates_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT ck_work_templates_resource_type CHECK (resource_type IN ('Space', 'Folder', 'List'))
);

CREATE INDEX IF NOT EXISTS ix_work_templates_workspace_id ON work.work_templates (workspace_id);

ALTER TABLE work.work_templates ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.work_templates FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON work.work_templates;
CREATE POLICY workspace_isolation ON work.work_templates USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

CREATE TABLE IF NOT EXISTS work.work_favorites (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    resource_type character varying(32) NOT NULL,
    resource_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_work_favorites PRIMARY KEY (id),
    CONSTRAINT fk_work_favorites_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_work_favorites_workspace_user_resource
    ON work.work_favorites (workspace_id, user_id, resource_type, resource_id);

ALTER TABLE work.work_favorites ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.work_favorites FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON work.work_favorites;
CREATE POLICY workspace_isolation ON work.work_favorites USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
