-- Planvexa DbUp script 0063_AddOAuthApplications.sql
-- Integrations/API/importers: OAuth2 applications + issued tokens, so a third-party app can
-- request scoped access to a workspace via the authorization-code flow (Planvexa as the OAuth PROVIDER).
-- Same workspace_id NOT NULL + RLS shape used since 0029/0030 (see 0062's header for the exact pattern).
--
-- integrations.oauth_applications: a workspace-owned OAuth2 client. Only the SHA-256 hash of the client
-- secret is stored (OAuthApplication.ClientSecretHash), mirroring integrations.personal_access_tokens'
-- token_hash column.
--
-- integrations.oauth_authorization_codes: short-lived, single-use codes minted by /oauth/authorize and
-- redeemed by /oauth/token. Only the code hash is stored.
--
-- integrations.oauth_tokens: issued access/refresh token pairs, scoped and workspace-isolated exactly
-- like a personal access token. Only hashes are stored.
--
-- CREATE TABLE IF NOT EXISTS / CREATE INDEX IF NOT EXISTS: safe on both an empty database and the
-- current already-migrated dev database (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS integrations.oauth_applications (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    client_id character varying(64) NOT NULL,
    client_secret_hash character varying(128) NOT NULL,
    redirect_uris_csv character varying(2048) NOT NULL,
    allowed_scopes_csv character varying(512) NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_oauth_applications PRIMARY KEY (id),
    CONSTRAINT fk_oauth_applications_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_oauth_applications_client_id ON integrations.oauth_applications (client_id);
CREATE INDEX IF NOT EXISTS ix_oauth_applications_workspace_id ON integrations.oauth_applications (workspace_id);

CREATE TABLE IF NOT EXISTS integrations.oauth_authorization_codes (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    application_id uuid NOT NULL,
    user_id uuid NOT NULL,
    code_hash character varying(128) NOT NULL,
    redirect_uri character varying(2048) NOT NULL,
    scopes_csv character varying(512) NOT NULL,
    expires_at_utc timestamp with time zone NOT NULL,
    used_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_oauth_authorization_codes PRIMARY KEY (id),
    CONSTRAINT fk_oauth_authorization_codes_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_oauth_authorization_codes_applications_application_id FOREIGN KEY (application_id) REFERENCES integrations.oauth_applications (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_oauth_authorization_codes_code_hash ON integrations.oauth_authorization_codes (code_hash);
CREATE INDEX IF NOT EXISTS ix_oauth_authorization_codes_application_id ON integrations.oauth_authorization_codes (application_id);

CREATE TABLE IF NOT EXISTS integrations.oauth_tokens (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    application_id uuid NOT NULL,
    user_id uuid NOT NULL,
    access_token_hash character varying(128) NOT NULL,
    refresh_token_hash character varying(128),
    scopes_csv character varying(512) NOT NULL,
    expires_at_utc timestamp with time zone NOT NULL,
    refresh_expires_at_utc timestamp with time zone,
    revoked_at_utc timestamp with time zone,
    last_used_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_oauth_tokens PRIMARY KEY (id),
    CONSTRAINT fk_oauth_tokens_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_oauth_tokens_applications_application_id FOREIGN KEY (application_id) REFERENCES integrations.oauth_applications (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_oauth_tokens_access_token_hash ON integrations.oauth_tokens (access_token_hash);
CREATE UNIQUE INDEX IF NOT EXISTS ix_oauth_tokens_refresh_token_hash ON integrations.oauth_tokens (refresh_token_hash) WHERE refresh_token_hash IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_oauth_tokens_application_id ON integrations.oauth_tokens (application_id);

ALTER TABLE integrations.oauth_applications ENABLE ROW LEVEL SECURITY;
ALTER TABLE integrations.oauth_applications FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON integrations.oauth_applications;
CREATE POLICY workspace_isolation ON integrations.oauth_applications USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

ALTER TABLE integrations.oauth_authorization_codes ENABLE ROW LEVEL SECURITY;
ALTER TABLE integrations.oauth_authorization_codes FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON integrations.oauth_authorization_codes;
CREATE POLICY workspace_isolation ON integrations.oauth_authorization_codes USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

ALTER TABLE integrations.oauth_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE integrations.oauth_tokens FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON integrations.oauth_tokens;
CREATE POLICY workspace_isolation ON integrations.oauth_tokens USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
