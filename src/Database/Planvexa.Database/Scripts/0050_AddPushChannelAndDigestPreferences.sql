-- Planvexa DbUp script 0050_AddPushChannelAndDigestPreferences.sql
-- Collaboration polish: Push notification channel preference + per-workspace digest cadence.
--
-- notifications.notification_preferences gets a new `push` column alongside the existing inbox/email
-- toggles (default false: unlike inbox/email, push needs an explicit opt-in plus a registered device —
-- see NotificationPolicy.DefaultChannels). Safe on both an empty database and the current already-
-- migrated dev database (AGENTS.md rule 9).
--
-- notifications.digest_preferences: a user's digest cadence (Off/Daily/Weekly) for one workspace,
-- distinct from notification_preferences because cadence is a single global-per-workspace setting, not
-- scoped to an event type. last_sent_at_utc is scheduler bookkeeping (DigestRunner.IsDue). Same
-- workspace_id NOT NULL + sole workspace_isolation RLS policy pattern used by every workspace-owned
-- table since 0029/0030 (see 0049's header).

ALTER TABLE notifications.notification_preferences ADD COLUMN IF NOT EXISTS push boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS notifications.digest_preferences (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    frequency character varying(16) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    last_sent_at_utc timestamp with time zone,
    CONSTRAINT pk_digest_preferences PRIMARY KEY (id),
    CONSTRAINT fk_digest_preferences_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_digest_preferences_workspace_user ON notifications.digest_preferences (workspace_id, user_id);

ALTER TABLE notifications.digest_preferences ENABLE ROW LEVEL SECURITY;
ALTER TABLE notifications.digest_preferences FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON notifications.digest_preferences;
CREATE POLICY workspace_isolation ON notifications.digest_preferences USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
