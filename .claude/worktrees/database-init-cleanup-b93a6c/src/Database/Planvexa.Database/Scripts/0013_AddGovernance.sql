-- Planvexa DbUp script 0013_AddGovernance.sql
-- Restores governance table creation (originally part of AddBillingAndGovernance, generated from EF
-- Core migration 20260729195006_AddBillingAndGovernance). Billing tables are intentionally omitted --
-- the Billing module has been removed. workspace_id is included from creation (final IWorkspaceOwned
-- shape) rather than added by a later backfill migration, since this is a fresh-install baseline.

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'governance') THEN
        CREATE SCHEMA governance;
    END IF;
END $$;

CREATE TABLE governance.enterprise_security_settings (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    sso_enabled boolean NOT NULL,
    saml_entity_id character varying(256),
    saml_metadata_url character varying(2048),
    scim_enabled boolean NOT NULL,
    scim_token_hash character varying(128),
    mfa_required boolean NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_enterprise_security_settings PRIMARY KEY (id)
);

CREATE TABLE governance.export_jobs (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    dataset character varying(32) NOT NULL,
    requested_by_user_id uuid NOT NULL,
    status character varying(16) NOT NULL,
    artifact text,
    row_count integer,
    error character varying(2000),
    created_at_utc timestamp with time zone NOT NULL,
    completed_at_utc timestamp with time zone,
    CONSTRAINT pk_export_jobs PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ix_enterprise_security_settings_tenant_id ON governance.enterprise_security_settings (tenant_id);

CREATE INDEX ix_export_jobs_tenant_id_status ON governance.export_jobs (tenant_id, status);

CREATE INDEX ix_export_jobs_tenant_id_workspace_id_created_at_utc ON governance.export_jobs (tenant_id, workspace_id, created_at_utc);
