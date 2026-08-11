-- Planvexa DbUp script 0065_AddImportJobs.sql
-- Integrations/API/importers: resumable bulk data imports (CSV/Excel/Trello/...). Lives in
-- the `work` schema (not `integrations`) because ImportJobService lives in the WorkManagement module —
-- every write it makes must go through the same Space/List/Task authorization gate manual creation uses
-- (see ImportJobService's doc comment), which only WorkManagement's own services provide.
--
-- work.import_jobs: one row per upload; column_mapping_json/detected columns support the CSV/Excel
-- column-mapping UI step; target_space_name/target_list_name are the fallback target for rows that don't
-- carry their own (a flat CSV/Excel sheet), while status is a string (not an int) so the journal reads
-- clearly, matching automation.automation_runs' status column.
--
-- work.import_job_rows: one row per source record; idempotency_key (job id + row index) plus the
-- Committed/CreatedTaskId columns are what make a commit resumable — re-invoking commit after an
-- interruption only touches rows not already Committed (AGENTS.md rule 13).
--
-- CREATE TABLE IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS work.import_jobs (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    source_type character varying(32) NOT NULL,
    file_name character varying(260) NOT NULL,
    status character varying(24) NOT NULL,
    column_mapping_json jsonb,
    detected_columns_csv text,
    target_space_name character varying(200),
    target_list_name character varying(200),
    target_space_id uuid,
    target_list_id uuid,
    total_rows integer NOT NULL DEFAULT 0,
    processed_rows integer NOT NULL DEFAULT 0,
    error_count integer NOT NULL DEFAULT 0,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_import_jobs PRIMARY KEY (id),
    CONSTRAINT fk_import_jobs_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_import_jobs_workspace_id_status ON work.import_jobs (workspace_id, status);

CREATE TABLE IF NOT EXISTS work.import_job_rows (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    import_job_id uuid NOT NULL,
    row_index integer NOT NULL,
    idempotency_key character varying(128) NOT NULL,
    raw_fields_json jsonb NOT NULL,
    status character varying(24) NOT NULL,
    error_message character varying(1000),
    created_task_id uuid,
    CONSTRAINT pk_import_job_rows PRIMARY KEY (id),
    CONSTRAINT fk_import_job_rows_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_import_job_rows_import_jobs_import_job_id FOREIGN KEY (import_job_id) REFERENCES work.import_jobs (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_import_job_rows_import_job_id_idempotency_key ON work.import_job_rows (import_job_id, idempotency_key);
CREATE INDEX IF NOT EXISTS ix_import_job_rows_import_job_id_status ON work.import_job_rows (import_job_id, status);

ALTER TABLE work.import_jobs ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.import_jobs FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON work.import_jobs;
CREATE POLICY workspace_isolation ON work.import_jobs USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

ALTER TABLE work.import_job_rows ENABLE ROW LEVEL SECURITY;
ALTER TABLE work.import_job_rows FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON work.import_job_rows;
CREATE POLICY workspace_isolation ON work.import_job_rows USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
