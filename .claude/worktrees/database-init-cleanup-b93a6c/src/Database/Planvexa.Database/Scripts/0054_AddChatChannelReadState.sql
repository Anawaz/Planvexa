-- Planvexa DbUp script 0054_AddChatChannelReadState.sql
-- Chat overhaul: per-user last-read position per channel, the primitive behind unread counts
-- in the channel-list sidebar. One row per (channel, user); the application upserts on view rather than
-- inserting a duplicate. Same workspace_id NOT NULL + sole workspace_isolation RLS policy pattern used by
-- every workspace-owned table since 0029/0030 (see 0049/0051/0053's headers). IF NOT EXISTS guards
-- throughout: safe on both an empty database and the current already-migrated dev database (AGENTS.md
-- rule 9) — brand-new table, nothing to backfill.

CREATE TABLE IF NOT EXISTS chat.channel_read_states (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    channel_id uuid NOT NULL,
    user_id uuid NOT NULL,
    last_read_message_id uuid,
    last_read_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_channel_read_states PRIMARY KEY (id),
    CONSTRAINT fk_channel_read_states_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_channel_read_states_channels_channel_id FOREIGN KEY (channel_id) REFERENCES chat.channels (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_channel_read_states_channel_id_user_id ON chat.channel_read_states (channel_id, user_id);
CREATE INDEX IF NOT EXISTS ix_channel_read_states_workspace_id_user_id ON chat.channel_read_states (workspace_id, user_id);

ALTER TABLE chat.channel_read_states ENABLE ROW LEVEL SECURITY;
ALTER TABLE chat.channel_read_states FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON chat.channel_read_states;
CREATE POLICY workspace_isolation ON chat.channel_read_states USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
