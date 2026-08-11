-- Planvexa DbUp script 0009_AddPlanningAndReporting.sql
-- Generated from EF Core migration 20260729145658_AddPlanningAndReporting. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'reporting') THEN
        CREATE SCHEMA reporting;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'planning') THEN
        CREATE SCHEMA planning;
    END IF;
END $$;

CREATE TABLE reporting.dashboards (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    is_private boolean NOT NULL,
    owner_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_dashboards PRIMARY KEY (id),
    CONSTRAINT ak_dashboards_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE planning.holidays (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    date_utc date NOT NULL,
    name character varying(200) NOT NULL,
    CONSTRAINT pk_holidays PRIMARY KEY (id)
);

CREATE TABLE planning.leave_entries (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    start_date date NOT NULL,
    end_date date NOT NULL,
    type character varying(16) NOT NULL,
    CONSTRAINT pk_leave_entries PRIMARY KEY (id)
);

CREATE TABLE planning.sprints (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    start_date date NOT NULL,
    end_date date NOT NULL,
    status character varying(16) NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_sprints PRIMARY KEY (id),
    CONSTRAINT ak_sprints_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE planning.task_estimates (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    estimate_seconds bigint NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_task_estimates PRIMARY KEY (id)
);

CREATE TABLE planning.work_schedules (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    working_days_mask integer NOT NULL,
    daily_capacity_hours numeric(6,2) NOT NULL,
    CONSTRAINT pk_work_schedules PRIMARY KEY (id)
);

CREATE TABLE reporting.dashboard_widgets (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    dashboard_id uuid NOT NULL,
    type character varying(32) NOT NULL,
    config_json jsonb NOT NULL,
    position integer NOT NULL,
    CONSTRAINT pk_dashboard_widgets PRIMARY KEY (id),
    CONSTRAINT fk_dashboard_widgets_dashboards_dashboard_id FOREIGN KEY (dashboard_id) REFERENCES reporting.dashboards (id) ON DELETE CASCADE
);

CREATE TABLE planning.sprint_items (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    sprint_id uuid NOT NULL,
    task_id uuid NOT NULL,
    points integer,
    CONSTRAINT pk_sprint_items PRIMARY KEY (id),
    CONSTRAINT fk_sprint_items_sprints_sprint_id FOREIGN KEY (sprint_id) REFERENCES planning.sprints (id) ON DELETE CASCADE
);

CREATE INDEX ix_dashboard_widgets_dashboard_id ON reporting.dashboard_widgets (dashboard_id);

CREATE INDEX ix_dashboard_widgets_tenant_id_dashboard_id ON reporting.dashboard_widgets (tenant_id, dashboard_id);

CREATE INDEX ix_dashboards_tenant_id_workspace_id ON reporting.dashboards (tenant_id, workspace_id);

CREATE INDEX ix_holidays_tenant_id_workspace_id_date_utc ON planning.holidays (tenant_id, workspace_id, date_utc);

CREATE INDEX ix_leave_entries_tenant_id_workspace_id_user_id_start_date ON planning.leave_entries (tenant_id, workspace_id, user_id, start_date);

CREATE INDEX ix_sprint_items_sprint_id ON planning.sprint_items (sprint_id);

CREATE UNIQUE INDEX ix_sprint_items_tenant_id_sprint_id_task_id ON planning.sprint_items (tenant_id, sprint_id, task_id);

CREATE INDEX ix_sprints_tenant_id_workspace_id ON planning.sprints (tenant_id, workspace_id);

CREATE UNIQUE INDEX ix_task_estimates_tenant_id_workspace_id_task_id ON planning.task_estimates (tenant_id, workspace_id, task_id);

CREATE UNIQUE INDEX ix_work_schedules_tenant_id_workspace_id ON planning.work_schedules (tenant_id, workspace_id);
