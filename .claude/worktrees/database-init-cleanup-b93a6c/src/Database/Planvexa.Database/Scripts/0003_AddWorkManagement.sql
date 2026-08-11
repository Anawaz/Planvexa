-- Planvexa DbUp script 0003_AddWorkManagement.sql
-- Generated from EF Core migration 20260729082743_AddWorkManagement. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'work') THEN
        CREATE SCHEMA work;
    END IF;
END $$;

CREATE TABLE work.custom_field_definitions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    scope character varying(16) NOT NULL,
    scope_id uuid,
    name character varying(200) NOT NULL,
    type character varying(24) NOT NULL,
    is_required boolean NOT NULL,
    position double precision NOT NULL,
    CONSTRAINT pk_custom_field_definitions PRIMARY KEY (id),
    CONSTRAINT ak_custom_field_definitions_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE work.recurring_task_definitions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    list_id uuid NOT NULL,
    title character varying(500) NOT NULL,
    description text,
    priority character varying(16) NOT NULL,
    frequency character varying(16) NOT NULL,
    interval integer NOT NULL,
    time_zone_id character varying(64) NOT NULL,
    anchor_utc timestamp with time zone NOT NULL,
    next_run_utc timestamp with time zone NOT NULL,
    last_generated_utc timestamp with time zone,
    created_by_user_id uuid NOT NULL,
    is_active boolean NOT NULL,
    CONSTRAINT pk_recurring_task_definitions PRIMARY KEY (id),
    CONSTRAINT ak_recurring_task_definition_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE work.saved_views (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    scope_type character varying(16) NOT NULL,
    scope_id uuid,
    name character varying(200) NOT NULL,
    view_type character varying(16) NOT NULL,
    config_json jsonb NOT NULL,
    is_private boolean NOT NULL,
    owner_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone,
    CONSTRAINT pk_saved_views PRIMARY KEY (id)
);

CREATE TABLE work.spaces (
    id uuid NOT NULL,
    name character varying(200) NOT NULL,
    description text,
    color character varying(32),
    icon character varying(64),
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    position double precision NOT NULL,
    is_archived boolean NOT NULL,
    is_deleted boolean NOT NULL,
    deleted_at_utc timestamp with time zone,
    deleted_by_user_id uuid,
    created_at_utc timestamp with time zone NOT NULL,
    created_by_user_id uuid,
    updated_at_utc timestamp with time zone,
    updated_by_user_id uuid,
    CONSTRAINT pk_spaces PRIMARY KEY (id),
    CONSTRAINT ak_space_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE work.status_schemes (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(128) NOT NULL,
    is_default boolean NOT NULL,
    CONSTRAINT pk_status_schemes PRIMARY KEY (id)
);

CREATE TABLE work.tags (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    color character varying(32) NOT NULL,
    CONSTRAINT pk_tags PRIMARY KEY (id),
    CONSTRAINT ak_tags_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE work.task_activity_events (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    actor_user_id uuid,
    type character varying(64) NOT NULL,
    data character varying(2000),
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_task_activity_events PRIMARY KEY (id)
);

CREATE TABLE work.task_checklists (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    task_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    position double precision NOT NULL,
    CONSTRAINT pk_task_checklists PRIMARY KEY (id)
);

CREATE TABLE work.task_dependencies (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    task_id uuid NOT NULL,
    depends_on_task_id uuid NOT NULL,
    type character varying(16) NOT NULL,
    CONSTRAINT pk_task_dependencies PRIMARY KEY (id)
);

CREATE TABLE work.custom_field_options (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    definition_id uuid NOT NULL,
    label character varying(200) NOT NULL,
    color character varying(32),
    position double precision NOT NULL,
    CONSTRAINT pk_custom_field_options PRIMARY KEY (id),
    CONSTRAINT fk_custom_field_options_custom_field_definitions_definition_id FOREIGN KEY (definition_id) REFERENCES work.custom_field_definitions (id) ON DELETE CASCADE
);

CREATE TABLE work.custom_field_values (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    task_id uuid NOT NULL,
    definition_id uuid NOT NULL,
    text_value character varying(4000),
    number_value numeric,
    date_value timestamp with time zone,
    bool_value boolean,
    option_id uuid,
    json_value jsonb,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_custom_field_values PRIMARY KEY (id),
    CONSTRAINT fk_custom_field_values_custom_field_definitions_tenant_id_defi FOREIGN KEY (tenant_id, definition_id) REFERENCES work.custom_field_definitions (tenant_id, id) ON DELETE CASCADE
);

CREATE TABLE work.recurring_occurrences (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    definition_id uuid NOT NULL,
    occurrence_key character varying(128) NOT NULL,
    generated_task_id uuid NOT NULL,
    generated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_recurring_occurrences PRIMARY KEY (id),
    CONSTRAINT fk_recurring_occurrences_recurring_task_definition_tenant_id_d FOREIGN KEY (tenant_id, definition_id) REFERENCES work.recurring_task_definitions (tenant_id, id) ON DELETE CASCADE
);

CREATE TABLE work.folders (
    id uuid NOT NULL,
    space_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    position double precision NOT NULL,
    is_archived boolean NOT NULL,
    is_deleted boolean NOT NULL,
    deleted_at_utc timestamp with time zone,
    deleted_by_user_id uuid,
    created_at_utc timestamp with time zone NOT NULL,
    created_by_user_id uuid,
    updated_at_utc timestamp with time zone,
    updated_by_user_id uuid,
    CONSTRAINT pk_folders PRIMARY KEY (id),
    CONSTRAINT ak_folders_tenant_id_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_folders_space_tenant_id_space_id FOREIGN KEY (tenant_id, space_id) REFERENCES work.spaces (tenant_id, id) ON DELETE CASCADE
);

CREATE TABLE work.lists (
    id uuid NOT NULL,
    space_id uuid NOT NULL,
    folder_id uuid,
    name character varying(200) NOT NULL,
    description text,
    status_scheme_id uuid NOT NULL,
    task_counter integer NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    position double precision NOT NULL,
    is_archived boolean NOT NULL,
    is_deleted boolean NOT NULL,
    deleted_at_utc timestamp with time zone,
    deleted_by_user_id uuid,
    created_at_utc timestamp with time zone NOT NULL,
    created_by_user_id uuid,
    updated_at_utc timestamp with time zone,
    updated_by_user_id uuid,
    CONSTRAINT pk_lists PRIMARY KEY (id),
    CONSTRAINT ak_lists_tenant_id_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_lists_spaces_tenant_id_space_id FOREIGN KEY (tenant_id, space_id) REFERENCES work.spaces (tenant_id, id) ON DELETE CASCADE
);

CREATE TABLE work.statuses (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    scheme_id uuid NOT NULL,
    name character varying(128) NOT NULL,
    category character varying(32) NOT NULL,
    color character varying(32) NOT NULL,
    position double precision NOT NULL,
    CONSTRAINT pk_statuses PRIMARY KEY (id),
    CONSTRAINT fk_statuses_status_schemes_scheme_id FOREIGN KEY (scheme_id) REFERENCES work.status_schemes (id) ON DELETE CASCADE
);

CREATE TABLE work.task_checklist_items (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    checklist_id uuid NOT NULL,
    content character varying(1000) NOT NULL,
    is_resolved boolean NOT NULL,
    position double precision NOT NULL,
    CONSTRAINT pk_task_checklist_items PRIMARY KEY (id),
    CONSTRAINT fk_task_checklist_items_task_checklists_checklist_id FOREIGN KEY (checklist_id) REFERENCES work.task_checklists (id) ON DELETE CASCADE
);

CREATE TABLE work.tasks (
    id uuid NOT NULL,
    space_id uuid NOT NULL,
    list_id uuid NOT NULL,
    parent_id uuid,
    sequence bigint NOT NULL,
    title character varying(500) NOT NULL,
    description text,
    status_id uuid NOT NULL,
    priority character varying(16) NOT NULL,
    start_date timestamp with time zone,
    due_date timestamp with time zone,
    is_milestone boolean NOT NULL,
    is_completed boolean NOT NULL,
    completed_at_utc timestamp with time zone,
    completed_by_user_id uuid,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    position double precision NOT NULL,
    is_archived boolean NOT NULL,
    is_deleted boolean NOT NULL,
    deleted_at_utc timestamp with time zone,
    deleted_by_user_id uuid,
    created_at_utc timestamp with time zone NOT NULL,
    created_by_user_id uuid,
    updated_at_utc timestamp with time zone,
    updated_by_user_id uuid,
    CONSTRAINT pk_tasks PRIMARY KEY (id),
    CONSTRAINT ak_tasks_tenant_id_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_tasks_lists_tenant_id_list_id FOREIGN KEY (tenant_id, list_id) REFERENCES work.lists (tenant_id, id) ON DELETE CASCADE
);

CREATE TABLE work.task_assignees (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    task_id uuid NOT NULL,
    user_id uuid NOT NULL,
    assigned_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_task_assignees PRIMARY KEY (id),
    CONSTRAINT fk_task_assignees_tasks_task_id FOREIGN KEY (task_id) REFERENCES work.tasks (id) ON DELETE CASCADE
);

CREATE TABLE work.task_tags (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    task_id uuid NOT NULL,
    tag_id uuid NOT NULL,
    CONSTRAINT pk_task_tags PRIMARY KEY (id),
    CONSTRAINT fk_task_tags_tags_tenant_id_tag_id FOREIGN KEY (tenant_id, tag_id) REFERENCES work.tags (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_task_tags_tasks_task_id FOREIGN KEY (task_id) REFERENCES work.tasks (id) ON DELETE CASCADE
);

CREATE TABLE work.task_watchers (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    task_id uuid NOT NULL,
    user_id uuid NOT NULL,
    added_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_task_watchers PRIMARY KEY (id),
    CONSTRAINT fk_task_watchers_tasks_task_id FOREIGN KEY (task_id) REFERENCES work.tasks (id) ON DELETE CASCADE
);

CREATE INDEX ix_custom_field_definitions_tenant_id_workspace_id ON work.custom_field_definitions (tenant_id, workspace_id);

CREATE INDEX ix_custom_field_options_definition_id ON work.custom_field_options (definition_id);

CREATE INDEX ix_custom_field_options_tenant_id_definition_id ON work.custom_field_options (tenant_id, definition_id);

CREATE INDEX ix_custom_field_values_tenant_id_definition_id_date_value ON work.custom_field_values (tenant_id, definition_id, date_value);

CREATE INDEX ix_custom_field_values_tenant_id_definition_id_number_value ON work.custom_field_values (tenant_id, definition_id, number_value);

CREATE INDEX ix_custom_field_values_tenant_id_definition_id_option_id ON work.custom_field_values (tenant_id, definition_id, option_id);

CREATE UNIQUE INDEX ix_custom_field_values_tenant_id_task_id_definition_id ON work.custom_field_values (tenant_id, task_id, definition_id);

CREATE INDEX ix_folders_tenant_id_space_id ON work.folders (tenant_id, space_id);

CREATE INDEX ix_lists_tenant_id_space_id ON work.lists (tenant_id, space_id);

CREATE UNIQUE INDEX ix_recurring_occurrences_tenant_id_definition_id_occurrence_key ON work.recurring_occurrences (tenant_id, definition_id, occurrence_key);

CREATE INDEX ix_recurring_task_definitions_is_active_next_run_utc ON work.recurring_task_definitions (is_active, next_run_utc);

CREATE INDEX ix_recurring_task_definitions_tenant_id_list_id ON work.recurring_task_definitions (tenant_id, list_id);

CREATE INDEX ix_saved_views_tenant_id_workspace_id ON work.saved_views (tenant_id, workspace_id);

CREATE INDEX ix_spaces_tenant_id_workspace_id ON work.spaces (tenant_id, workspace_id);

CREATE INDEX ix_status_schemes_tenant_id_workspace_id ON work.status_schemes (tenant_id, workspace_id);

CREATE INDEX ix_statuses_scheme_id ON work.statuses (scheme_id);

CREATE INDEX ix_statuses_tenant_id_scheme_id ON work.statuses (tenant_id, scheme_id);

CREATE UNIQUE INDEX ix_tags_tenant_id_workspace_id_name ON work.tags (tenant_id, workspace_id, name);

CREATE INDEX ix_task_activity_events_tenant_id_task_id_created_at_utc ON work.task_activity_events (tenant_id, task_id, created_at_utc);

CREATE INDEX ix_task_assignees_task_id ON work.task_assignees (task_id);

CREATE UNIQUE INDEX ix_task_assignees_tenant_id_task_id_user_id ON work.task_assignees (tenant_id, task_id, user_id);

CREATE INDEX ix_task_assignees_tenant_id_user_id ON work.task_assignees (tenant_id, user_id);

CREATE INDEX ix_task_checklist_items_checklist_id ON work.task_checklist_items (checklist_id);

CREATE INDEX ix_task_checklist_items_tenant_id_checklist_id ON work.task_checklist_items (tenant_id, checklist_id);

CREATE INDEX ix_task_checklists_tenant_id_task_id ON work.task_checklists (tenant_id, task_id);

CREATE INDEX ix_task_dependencies_tenant_id_depends_on_task_id ON work.task_dependencies (tenant_id, depends_on_task_id);

CREATE UNIQUE INDEX ix_task_dependencies_tenant_id_task_id_depends_on_task_id_type ON work.task_dependencies (tenant_id, task_id, depends_on_task_id, type);

CREATE INDEX ix_task_tags_task_id ON work.task_tags (task_id);

CREATE INDEX ix_task_tags_tenant_id_tag_id ON work.task_tags (tenant_id, tag_id);

CREATE UNIQUE INDEX ix_task_tags_tenant_id_task_id_tag_id ON work.task_tags (tenant_id, task_id, tag_id);

CREATE INDEX ix_task_watchers_task_id ON work.task_watchers (task_id);

CREATE UNIQUE INDEX ix_task_watchers_tenant_id_task_id_user_id ON work.task_watchers (tenant_id, task_id, user_id);

CREATE INDEX ix_tasks_tenant_id_list_id_position ON work.tasks (tenant_id, list_id, position);

CREATE INDEX ix_tasks_tenant_id_list_id_status_id ON work.tasks (tenant_id, list_id, status_id);

CREATE INDEX ix_tasks_tenant_id_parent_id ON work.tasks (tenant_id, parent_id);

CREATE INDEX ix_tasks_tenant_id_workspace_id ON work.tasks (tenant_id, workspace_id);
