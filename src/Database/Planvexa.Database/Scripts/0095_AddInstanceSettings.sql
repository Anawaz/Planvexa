-- Planvexa DbUp script 0095_AddInstanceSettings.sql
-- Installation-wide settings, editable from the host administration console. Exactly one row, keyed
-- by a fixed id rather than anything meaningful -- these describe the installation, not a Workspace,
-- so there is nothing to key them by. The CHECK constraint is what makes "exactly one" a database
-- guarantee rather than a convention.
--
-- No RLS, deliberately: same posture as identity.users and platform.outbox_messages. A
-- workspace-keyed policy is meaningless for a single global row, and these values are read on paths
-- that have no ambient Workspace at all -- including GET /api/v1/public/registration-policy, which is
-- anonymous. Write access is the host-admin endpoint policy.
--
-- The row is deliberately NOT seeded here. It is created on first read by
-- InstanceSettingsService.LoadAsync, which seeds allow_self_registration from the operator's existing
-- Registration:AllowSelfRegistration configuration value -- SQL cannot read appsettings, so seeding a
-- hardcoded `true` here would silently re-open self-registration on every installation that had
-- switched it off in config. After that first read the row owns the value and the configuration key is
-- only a default for the next fresh install.
--
-- CREATE TABLE IF NOT EXISTS keeps this safe on an empty and an already-migrated database
-- (AGENTS.md rule 9).

CREATE TABLE IF NOT EXISTS platform.instance_settings (
    id integer NOT NULL,
    allow_self_registration boolean NOT NULL DEFAULT true,
    workspace_creation_policy character varying(32) NOT NULL DEFAULT 'Anyone',
    instance_name character varying(200),
    logo_url character varying(500),
    support_email character varying(320),
    updated_at_utc timestamp with time zone,
    updated_by_user_id uuid,
    CONSTRAINT pk_instance_settings PRIMARY KEY (id),
    CONSTRAINT ck_instance_settings_singleton CHECK (id = 1),
    CONSTRAINT ck_instance_settings_workspace_creation_policy
        CHECK (workspace_creation_policy IN ('Anyone', 'HostAdminsOnly'))
);
