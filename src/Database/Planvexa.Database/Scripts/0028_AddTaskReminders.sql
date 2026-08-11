-- Task reminders: one-shot per-user reminders that fire as a notification at remind_at_utc.
--
-- RLS mirrors the hardened work-management pattern: the permissive tenant_isolation policy plus the
-- RESTRICTIVE workspace_isolation policy (as added in bulk by 0025 for tables that predate it), so a
-- row is visible/writable only when it matches BOTH the current tenant AND the current workspace.
-- A missing app.current_workspace (bootstrap / controlled background sweeps) does not further
-- restrict, so the reminder dispatcher can scan due rows before binding a workspace.

CREATE TABLE work.task_reminders (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    user_id uuid NOT NULL,
    remind_at_utc timestamp with time zone NOT NULL,
    note character varying(500),
    is_sent boolean NOT NULL,
    sent_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_task_reminders PRIMARY KEY (id),
    CONSTRAINT fk_task_reminders_tasks FOREIGN KEY (task_id) REFERENCES work.tasks (id) ON DELETE CASCADE
);

CREATE INDEX ix_task_reminders_tenant_id_task_id ON work.task_reminders (tenant_id, task_id);
CREATE INDEX ix_task_reminders_is_sent_remind_at_utc ON work.task_reminders (is_sent, remind_at_utc);

ALTER TABLE work.task_reminders ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.task_reminders FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.task_reminders
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NOT NULL
    AND tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NOT NULL
    AND tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

CREATE POLICY workspace_isolation ON work.task_reminders AS RESTRICTIVE
USING (
    nullif(current_setting('app.current_workspace', true), '') IS NULL
    OR workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NULL
    OR workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
