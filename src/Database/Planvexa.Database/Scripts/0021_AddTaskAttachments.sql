-- Task attachments: metadata rows only. The bytes live behind IFileStorage (local disk today,
-- object storage later), addressed by storage_path.
--
-- RLS follows the hardened pattern from 0019/0020: a missing or empty app.current_tenant yields no
-- rows and blocks every write, so an unscoped connection can neither read nor insert attachments.

CREATE TABLE work.task_attachments (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    file_name character varying(260) NOT NULL,
    content_type character varying(200) NOT NULL,
    size_bytes bigint NOT NULL,
    storage_path character varying(500) NOT NULL,
    uploaded_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_task_attachments PRIMARY KEY (id),
    CONSTRAINT fk_task_attachments_tasks FOREIGN KEY (task_id) REFERENCES work.tasks (id) ON DELETE CASCADE
);

CREATE INDEX ix_task_attachments_tenant_id_task_id ON work.task_attachments (tenant_id, task_id);

ALTER TABLE work.task_attachments ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.task_attachments FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.task_attachments
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NOT NULL
    AND tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NOT NULL
    AND tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
