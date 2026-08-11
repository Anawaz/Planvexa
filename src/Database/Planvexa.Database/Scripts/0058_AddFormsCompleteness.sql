-- Planvexa DbUp script 0058_AddFormsCompleteness.sql
-- Forms completeness: branding, confirmation pages, spam/rate/submission limits, full task
-- routing (status/priority/tags/due date/team), conditional field logic, and custom-field mapping on
-- forms.forms/forms.form_fields; a respondent key on forms.form_submissions for per-respondent submission
-- caps; and a new forms.form_uploads table for File Upload fields (bytes live in IFileStorage, same
-- workspace_id NOT NULL + sole workspace_isolation RLS policy pattern used by every workspace-owned table
-- since 0029/0030 -- see 0053's header). All ADD COLUMN/CREATE ... IF NOT EXISTS: safe on both an empty
-- database and the current already-migrated dev database (AGENTS.md rule 9) -- new columns are all
-- nullable/optional settings with no data to backfill, and form_uploads is a brand-new table.

ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS branding_logo_url character varying(2000);
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS branding_color character varying(16);
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS confirmation_message character varying(2000);
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS confirmation_redirect_url character varying(2000);
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS min_submit_seconds integer;
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS max_total_submissions integer;
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS max_submissions_per_respondent integer;
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS target_status_name character varying(100);
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS target_priority character varying(20);
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS target_tags_csv character varying(1000);
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS target_team_id uuid;
ALTER TABLE forms.forms ADD COLUMN IF NOT EXISTS due_date_days_after_submission integer;

ALTER TABLE forms.form_fields ADD COLUMN IF NOT EXISTS condition_field_id uuid;
ALTER TABLE forms.form_fields ADD COLUMN IF NOT EXISTS condition_operator character varying(20);
ALTER TABLE forms.form_fields ADD COLUMN IF NOT EXISTS condition_value character varying(500);
ALTER TABLE forms.form_fields ADD COLUMN IF NOT EXISTS custom_field_definition_id uuid;

ALTER TABLE forms.form_submissions ADD COLUMN IF NOT EXISTS respondent_key character varying(128);

CREATE INDEX IF NOT EXISTS ix_form_submissions_form_id_respondent_key ON forms.form_submissions (form_id, respondent_key);

CREATE TABLE IF NOT EXISTS forms.form_uploads (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    form_id uuid NOT NULL,
    storage_path character varying(1000) NOT NULL,
    file_name character varying(300) NOT NULL,
    content_type character varying(200) NOT NULL,
    size_bytes bigint NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_form_uploads PRIMARY KEY (id),
    CONSTRAINT fk_form_uploads_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_form_uploads_forms_form_id FOREIGN KEY (form_id) REFERENCES forms.forms (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_form_uploads_form_id ON forms.form_uploads (form_id);

ALTER TABLE forms.form_uploads ENABLE ROW LEVEL SECURITY;
ALTER TABLE forms.form_uploads FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON forms.form_uploads;
CREATE POLICY workspace_isolation ON forms.form_uploads USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
