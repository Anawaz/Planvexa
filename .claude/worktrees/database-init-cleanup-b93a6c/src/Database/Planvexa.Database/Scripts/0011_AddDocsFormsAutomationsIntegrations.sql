-- Planvexa DbUp script 0011_AddDocsFormsAutomationsIntegrations.sql
-- Generated from EF Core migration 20260729185120_AddDocsFormsAutomationsIntegrations. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'automation') THEN
        CREATE SCHEMA automation;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'docs') THEN
        CREATE SCHEMA docs;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'forms') THEN
        CREATE SCHEMA forms;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'integrations') THEN
        CREATE SCHEMA integrations;
    END IF;
END $$;

CREATE TABLE automation.automation_rules (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    trigger_type character varying(64) NOT NULL,
    condition_json jsonb NOT NULL,
    action_json jsonb NOT NULL,
    is_enabled boolean NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_automation_rules PRIMARY KEY (id)
);

CREATE TABLE automation.automation_runs (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    rule_id uuid NOT NULL,
    event_id uuid NOT NULL,
    status character varying(16) NOT NULL,
    detail character varying(1000),
    occurred_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_automation_runs PRIMARY KEY (id)
);

CREATE TABLE docs.documents (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    owner_user_id uuid NOT NULL,
    title character varying(300) NOT NULL,
    content text NOT NULL,
    is_private boolean NOT NULL,
    space_id uuid,
    list_id uuid,
    task_id uuid,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_documents PRIMARY KEY (id),
    CONSTRAINT ak_documents_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE forms.form_submissions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    form_id uuid NOT NULL,
    created_task_id uuid,
    values_json jsonb NOT NULL,
    idempotency_key character varying(128) NOT NULL,
    submitted_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_form_submissions PRIMARY KEY (id)
);

CREATE TABLE forms.forms (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    list_id uuid NOT NULL,
    title character varying(300) NOT NULL,
    description character varying(2000),
    public_token character varying(64) NOT NULL,
    is_active boolean NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_forms PRIMARY KEY (id),
    CONSTRAINT ak_forms_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE integrations.personal_access_tokens (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    subject character varying(256) NOT NULL,
    email character varying(320) NOT NULL,
    display_name character varying(256) NOT NULL,
    name character varying(200) NOT NULL,
    token_hash character varying(128) NOT NULL,
    scopes_csv character varying(512) NOT NULL,
    expires_at_utc timestamp with time zone,
    last_used_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_personal_access_tokens PRIMARY KEY (id)
);

CREATE TABLE integrations.webhook_deliveries (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    subscription_id uuid NOT NULL,
    event_id uuid NOT NULL,
    event_type character varying(64) NOT NULL,
    attempt integer NOT NULL,
    success boolean NOT NULL,
    status_code integer,
    detail character varying(500),
    occurred_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_webhook_deliveries PRIMARY KEY (id)
);

CREATE TABLE integrations.webhook_subscriptions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    url character varying(2048) NOT NULL,
    secret character varying(128) NOT NULL,
    event_types_csv character varying(512) NOT NULL,
    is_active boolean NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_webhook_subscriptions PRIMARY KEY (id)
);

CREATE TABLE docs.document_versions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    author_user_id uuid NOT NULL,
    content text NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_document_versions PRIMARY KEY (id),
    CONSTRAINT fk_document_versions_documents_document_id FOREIGN KEY (document_id) REFERENCES docs.documents (id) ON DELETE CASCADE
);

CREATE TABLE forms.form_fields (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    form_id uuid NOT NULL,
    label character varying(200) NOT NULL,
    type character varying(16) NOT NULL,
    required boolean NOT NULL,
    options_csv character varying(2000) NOT NULL,
    position integer NOT NULL,
    CONSTRAINT pk_form_fields PRIMARY KEY (id),
    CONSTRAINT fk_form_fields_forms_form_id FOREIGN KEY (form_id) REFERENCES forms.forms (id) ON DELETE CASCADE
);

CREATE INDEX ix_automation_rules_tenant_id_workspace_id_trigger_type_is_ena ON automation.automation_rules (tenant_id, workspace_id, trigger_type, is_enabled);

CREATE UNIQUE INDEX ix_automation_runs_tenant_id_rule_id_event_id ON automation.automation_runs (tenant_id, rule_id, event_id);

CREATE INDEX ix_automation_runs_tenant_id_workspace_id_occurred_at_utc ON automation.automation_runs (tenant_id, workspace_id, occurred_at_utc);

CREATE INDEX ix_document_versions_document_id ON docs.document_versions (document_id);

CREATE INDEX ix_document_versions_tenant_id_document_id_created_at_utc ON docs.document_versions (tenant_id, document_id, created_at_utc);

CREATE INDEX ix_documents_tenant_id_workspace_id ON docs.documents (tenant_id, workspace_id);

CREATE INDEX ix_form_fields_form_id ON forms.form_fields (form_id);

CREATE INDEX ix_form_fields_tenant_id_form_id ON forms.form_fields (tenant_id, form_id);

CREATE UNIQUE INDEX ix_form_submissions_tenant_id_form_id_idempotency_key ON forms.form_submissions (tenant_id, form_id, idempotency_key);

CREATE INDEX ix_form_submissions_tenant_id_form_id_submitted_at_utc ON forms.form_submissions (tenant_id, form_id, submitted_at_utc);

CREATE UNIQUE INDEX ix_forms_public_token ON forms.forms (public_token);

CREATE INDEX ix_forms_tenant_id_workspace_id ON forms.forms (tenant_id, workspace_id);

CREATE INDEX ix_personal_access_tokens_tenant_id_user_id ON integrations.personal_access_tokens (tenant_id, user_id);

CREATE UNIQUE INDEX ix_personal_access_tokens_token_hash ON integrations.personal_access_tokens (token_hash);

CREATE UNIQUE INDEX ix_webhook_deliveries_tenant_id_subscription_id_event_id ON integrations.webhook_deliveries (tenant_id, subscription_id, event_id);

CREATE INDEX ix_webhook_deliveries_tenant_id_subscription_id_occurred_at_utc ON integrations.webhook_deliveries (tenant_id, subscription_id, occurred_at_utc);

CREATE INDEX ix_webhook_subscriptions_tenant_id_workspace_id_is_active ON integrations.webhook_subscriptions (tenant_id, workspace_id, is_active);
