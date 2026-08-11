-- Planvexa DbUp script 0015_AddAiMobileRetention.sql
-- Restores table creation for the Ai and Mobile schemas, and governance.retention_policies
-- (originally generated from EF Core migration 20260730064457_AddAiMobileRetention). mobile.device_registrations
-- and governance.retention_policies now include workspace_id from creation (final IWorkspaceOwned shape),
-- since this is a fresh-install baseline rather than an in-place upgrade.

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ai') THEN
        CREATE SCHEMA ai;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'mobile') THEN
        CREATE SCHEMA mobile;
    END IF;
END $$;

CREATE TABLE ai.ai_requests (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    request_key character varying(200) NOT NULL,
    kind character varying(24) NOT NULL,
    entity_id uuid NOT NULL,
    tokens_estimated integer NOT NULL,
    result text NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_ai_requests PRIMARY KEY (id)
);

CREATE TABLE mobile.device_registrations (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    platform character varying(16) NOT NULL,
    token_hash character varying(128) NOT NULL,
    app_version character varying(64),
    created_at_utc timestamp with time zone NOT NULL,
    last_seen_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_device_registrations PRIMARY KEY (id)
);

CREATE TABLE governance.retention_policies (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    deleted_task_retention_days integer NOT NULL,
    audit_retention_days integer NOT NULL,
    legal_hold boolean NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_retention_policies PRIMARY KEY (id)
);

CREATE INDEX ix_ai_requests_tenant_id_created_at_utc ON ai.ai_requests (tenant_id, created_at_utc);

CREATE UNIQUE INDEX ix_ai_requests_tenant_id_request_key ON ai.ai_requests (tenant_id, request_key);

CREATE INDEX ix_device_registrations_tenant_id_user_id ON mobile.device_registrations (tenant_id, user_id);

CREATE UNIQUE INDEX ix_device_registrations_tenant_id_user_id_token_hash ON mobile.device_registrations (tenant_id, user_id, token_hash);

CREATE UNIQUE INDEX ix_retention_policies_tenant_id ON governance.retention_policies (tenant_id);
