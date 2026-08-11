-- Planvexa DbUp baseline script

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'platform') THEN
        CREATE SCHEMA platform;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'audit') THEN
        CREATE SCHEMA audit;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'tenancy') THEN
        CREATE SCHEMA tenancy;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'identity') THEN
        CREATE SCHEMA identity;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'governance') THEN
        CREATE SCHEMA governance;
    END IF;
END $$;

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

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'notifications') THEN
        CREATE SCHEMA notifications;
    END IF;
END $$;

CREATE TABLE audit.audit_events (
    id uuid NOT NULL,
    tenant_id uuid,
    actor_user_id uuid,
    action character varying(128) NOT NULL,
    entity_type character varying(128) NOT NULL,
    entity_id uuid,
    data jsonb,
    correlation_id character varying(128),
    ip_address character varying(64),
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_audit_events PRIMARY KEY (id)
);

CREATE TABLE platform.outbox_messages (
    id uuid NOT NULL,
    tenant_id uuid,
    type character varying(512) NOT NULL,
    payload jsonb NOT NULL,
    occurred_on_utc timestamp with time zone NOT NULL,
    processed_on_utc timestamp with time zone,
    attempts integer NOT NULL,
    error character varying(2048),
    correlation_id character varying(128),
    CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
);

CREATE TABLE identity.users (
    id uuid NOT NULL,
    subject character varying(256) NOT NULL,
    email character varying(320) NOT NULL,
    display_name character varying(200) NOT NULL,
    is_active boolean NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone,
    last_seen_at_utc timestamp with time zone,
    CONSTRAINT pk_users PRIMARY KEY (id)
);

CREATE TABLE tenancy.tenants (
    id uuid NOT NULL,
    slug character varying(63) NOT NULL,
    name character varying(200) NOT NULL,
    region character varying(32) NOT NULL,
    status character varying(32) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_tenants PRIMARY KEY (id)
);

CREATE TABLE tenancy.workspaces (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    slug character varying(63) NOT NULL,
    status character varying(32) NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    workspace_id uuid NOT NULL,
    CONSTRAINT pk_workspaces PRIMARY KEY (id),
    CONSTRAINT ak_workspaces_tenant_id_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_workspaces_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES tenancy.tenants (id) ON DELETE CASCADE
);

CREATE TABLE tenancy.workspace_members (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    role character varying(32) NOT NULL,
    is_guest boolean NOT NULL,
    status character varying(32) NOT NULL,
    joined_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_workspace_members PRIMARY KEY (id),
    CONSTRAINT fk_workspace_members_workspaces_tenant_id_workspace_id FOREIGN KEY (tenant_id, workspace_id) REFERENCES tenancy.workspaces (tenant_id, id) ON DELETE CASCADE
);

CREATE TABLE tenancy.teams (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    description character varying(1000),
    is_archived boolean NOT NULL DEFAULT false,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_teams PRIMARY KEY (id),
    CONSTRAINT ak_teams_tenant_id_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_teams_workspaces_tenant_id_workspace_id FOREIGN KEY (tenant_id, workspace_id) REFERENCES tenancy.workspaces (tenant_id, id) ON DELETE CASCADE
);

CREATE INDEX ix_teams_tenant_id_workspace_id ON tenancy.teams (tenant_id, workspace_id);

CREATE TABLE tenancy.team_members (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    team_id uuid NOT NULL,
    user_id uuid NOT NULL,
    added_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_team_members PRIMARY KEY (id),
    CONSTRAINT uq_team_members_tenant_id_team_id_user_id UNIQUE (tenant_id, team_id, user_id),
    CONSTRAINT fk_team_members_teams_tenant_id_team_id FOREIGN KEY (tenant_id, team_id) REFERENCES tenancy.teams (tenant_id, id) ON DELETE CASCADE
);

CREATE TABLE tenancy.feature_entitlements (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    feature_key character varying(64) NOT NULL,
    is_enabled boolean NOT NULL,
    "limit" bigint,
    source character varying(64) NOT NULL,
    CONSTRAINT pk_feature_entitlements PRIMARY KEY (id),
    CONSTRAINT fk_feature_entitlements_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE TABLE tenancy.invitations (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    email character varying(320) NOT NULL,
    role character varying(32) NOT NULL,
    token_hash character varying(128) NOT NULL,
    status character varying(32) NOT NULL,
    invited_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    expires_at_utc timestamp with time zone NOT NULL,
    accepted_at_utc timestamp with time zone,
    accepted_by_user_id uuid,
    CONSTRAINT pk_invitations PRIMARY KEY (id),
    CONSTRAINT fk_invitations_workspaces_tenant_id_workspace_id FOREIGN KEY (tenant_id, workspace_id) REFERENCES tenancy.workspaces (tenant_id, id) ON DELETE CASCADE
);

CREATE INDEX ix_feature_entitlements_workspace_id_feature_key ON tenancy.feature_entitlements (workspace_id, feature_key);
CREATE UNIQUE INDEX ix_tenants_slug ON tenancy.tenants (slug);
CREATE UNIQUE INDEX ix_workspaces_tenant_id_slug ON tenancy.workspaces (tenant_id, slug);
CREATE INDEX ix_workspace_members_tenant_id_user_id ON tenancy.workspace_members (tenant_id, user_id);
CREATE UNIQUE INDEX ix_workspace_members_tenant_id_workspace_id_user_id ON tenancy.workspace_members (tenant_id, workspace_id, user_id);
CREATE UNIQUE INDEX ix_invitations_token_hash ON tenancy.invitations (token_hash);

ALTER TABLE audit.audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit.audit_events FORCE ROW LEVEL SECURITY;
CREATE POLICY audit_isolation ON audit.audit_events USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE tenancy.feature_entitlements ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy.feature_entitlements FORCE ROW LEVEL SECURITY;
CREATE POLICY feature_entitlement_isolation ON tenancy.feature_entitlements USING (
    nullif(current_setting('app.current_workspace', true), '') IS NULL
    OR workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NULL
    OR workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
