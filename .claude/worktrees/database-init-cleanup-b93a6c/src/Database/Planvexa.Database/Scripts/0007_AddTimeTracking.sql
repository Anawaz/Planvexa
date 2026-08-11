-- Planvexa DbUp script 0007_AddTimeTracking.sql
-- Generated from EF Core migration 20260729142058_AddTimeTracking. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'time') THEN
        CREATE SCHEMA time;
    END IF;
END $$;

CREATE TABLE time.member_rates (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    project_id uuid,
    billing_rate numeric(18,4) NOT NULL,
    cost_rate numeric(18,4) NOT NULL,
    CONSTRAINT pk_member_rates PRIMARY KEY (id)
);

CREATE TABLE time.time_entries (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    task_id uuid,
    started_at_utc timestamp with time zone NOT NULL,
    ended_at_utc timestamp with time zone,
    duration_seconds bigint NOT NULL,
    time_zone_id character varying(64) NOT NULL,
    description character varying(2000),
    is_billable boolean NOT NULL,
    billing_rate numeric(18,4) NOT NULL,
    cost_rate numeric(18,4) NOT NULL,
    source character varying(16) NOT NULL,
    approval_status character varying(16) NOT NULL,
    approved_by_user_id uuid,
    locked_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone,
    CONSTRAINT pk_time_entries PRIMARY KEY (id),
    CONSTRAINT ak_time_entries_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE time.time_entry_audits (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    time_entry_id uuid NOT NULL,
    actor_user_id uuid NOT NULL,
    action character varying(64) NOT NULL,
    detail character varying(512),
    reason character varying(1000),
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_time_entry_audits PRIMARY KEY (id)
);

CREATE TABLE time.time_policies (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    single_active_timer boolean NOT NULL,
    rounding_minutes integer NOT NULL,
    minimum_duration_seconds bigint NOT NULL,
    maximum_entry_seconds bigint NOT NULL,
    billable_by_default boolean NOT NULL,
    require_description boolean NOT NULL,
    require_task boolean NOT NULL,
    edit_window_hours integer NOT NULL,
    approval_required boolean NOT NULL,
    week_starts_on integer NOT NULL,
    lock_date_utc timestamp with time zone,
    overtime_threshold_seconds bigint NOT NULL,
    CONSTRAINT pk_time_policies PRIMARY KEY (id)
);

CREATE TABLE time.timesheet_periods (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    period_start_utc timestamp with time zone NOT NULL,
    period_end_utc timestamp with time zone NOT NULL,
    cadence character varying(16) NOT NULL,
    status character varying(16) NOT NULL,
    submitted_at_utc timestamp with time zone,
    approved_by_user_id uuid,
    decided_at_utc timestamp with time zone,
    CONSTRAINT pk_timesheet_periods PRIMARY KEY (id),
    CONSTRAINT ak_timesheet_periods_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE time.timesheet_approvals (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    period_id uuid NOT NULL,
    approver_user_id uuid NOT NULL,
    approved boolean NOT NULL,
    comment character varying(2000),
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_timesheet_approvals PRIMARY KEY (id),
    CONSTRAINT fk_timesheet_approvals_timesheet_periods_period_id FOREIGN KEY (period_id) REFERENCES time.timesheet_periods (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ix_member_rates_tenant_id_workspace_id_user_id_project_id ON time.member_rates (tenant_id, workspace_id, user_id, project_id);

CREATE INDEX ix_time_entries_tenant_id_task_id ON time.time_entries (tenant_id, task_id);

CREATE INDEX ix_time_entries_tenant_id_user_id_started_at_utc ON time.time_entries (tenant_id, user_id, started_at_utc);

CREATE INDEX ix_time_entries_tenant_id_workspace_id_started_at_utc ON time.time_entries (tenant_id, workspace_id, started_at_utc);

CREATE UNIQUE INDEX ux_time_entries_single_active_timer ON time.time_entries (tenant_id, user_id) WHERE ended_at_utc IS NULL;

CREATE INDEX ix_time_entry_audits_tenant_id_time_entry_id_created_at_utc ON time.time_entry_audits (tenant_id, time_entry_id, created_at_utc);

CREATE UNIQUE INDEX ix_time_policies_tenant_id_workspace_id ON time.time_policies (tenant_id, workspace_id);

CREATE INDEX ix_timesheet_approvals_period_id ON time.timesheet_approvals (period_id);

CREATE INDEX ix_timesheet_approvals_tenant_id_period_id ON time.timesheet_approvals (tenant_id, period_id);

CREATE UNIQUE INDEX ix_timesheet_periods_tenant_id_workspace_id_user_id_period_sta ON time.timesheet_periods (tenant_id, workspace_id, user_id, period_start_utc);
