-- Planvexa DbUp script 0064_AddIntegrationProviderSettings.sql
-- Integrations/API/importers: one generic settings row per (workspace, provider) for every
-- third-party integration provider (Slack, GitHub, Google Calendar, Outlook Calendar, Teams, GitLab,
-- Google Drive, OneDrive, SharePoint) — same optional/workspace-configured/encrypted-secret shape as
-- ai.provider_settings (see AiProviderSettings), one table for every provider rather than nine
-- near-identical ones (AGENTS.md rule 16). config_json holds non-secret provider fields (e.g. GitHub's
-- owner/repo); secret_encrypted holds the one sensitive credential, encrypted the same way
-- ai.provider_settings.api_key_encrypted is.
--
-- CREATE TABLE IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS integrations.provider_settings (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    provider character varying(64) NOT NULL,
    config_json jsonb NOT NULL DEFAULT '{}',
    secret_encrypted text NOT NULL DEFAULT '',
    is_enabled boolean NOT NULL DEFAULT false,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_integration_provider_settings PRIMARY KEY (id),
    CONSTRAINT fk_integration_provider_settings_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_integration_provider_settings_workspace_id_provider ON integrations.provider_settings (workspace_id, provider);

ALTER TABLE integrations.provider_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE integrations.provider_settings FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON integrations.provider_settings;
CREATE POLICY workspace_isolation ON integrations.provider_settings USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
