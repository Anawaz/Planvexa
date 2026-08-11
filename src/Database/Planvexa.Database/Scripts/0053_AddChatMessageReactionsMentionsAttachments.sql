-- Planvexa DbUp script 0053_AddChatMessageReactionsMentionsAttachments.sql
-- Chat overhaul: reactions, mentions, and attachments on chat messages — same shapes as
-- Collaboration's collab.comment_reactions/collab.mentions and WorkManagement's work.task_attachments
-- (see those tables' definitions in earlier scripts), just scoped to chat.messages instead. Same
-- workspace_id NOT NULL + sole workspace_isolation RLS policy pattern used by every workspace-owned table
-- since 0029/0030 (see 0049/0051's headers). IF NOT EXISTS guards throughout: safe on both an empty
-- database and the current already-migrated dev database (AGENTS.md rule 9) — there is no prior data to
-- backfill since these are brand-new tables.

CREATE TABLE IF NOT EXISTS chat.mentions (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    message_id uuid NOT NULL,
    mentioned_user_id uuid NOT NULL,
    CONSTRAINT pk_mentions PRIMARY KEY (id),
    CONSTRAINT fk_mentions_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_mentions_messages_message_id FOREIGN KEY (message_id) REFERENCES chat.messages (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_mentions_workspace_id_mentioned_user_id ON chat.mentions (workspace_id, mentioned_user_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_mentions_message_id_mentioned_user_id ON chat.mentions (message_id, mentioned_user_id);

CREATE TABLE IF NOT EXISTS chat.message_reactions (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    message_id uuid NOT NULL,
    user_id uuid NOT NULL,
    emoji character varying(32) NOT NULL,
    CONSTRAINT pk_message_reactions PRIMARY KEY (id),
    CONSTRAINT fk_message_reactions_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_message_reactions_messages_message_id FOREIGN KEY (message_id) REFERENCES chat.messages (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_message_reactions_message_id_user_id_emoji ON chat.message_reactions (message_id, user_id, emoji);

CREATE TABLE IF NOT EXISTS chat.attachments (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    message_id uuid NOT NULL,
    file_name character varying(260) NOT NULL,
    content_type character varying(200) NOT NULL,
    size_bytes bigint NOT NULL,
    storage_path character varying(1000) NOT NULL,
    uploaded_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_attachments PRIMARY KEY (id),
    CONSTRAINT fk_attachments_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_attachments_messages_message_id FOREIGN KEY (message_id) REFERENCES chat.messages (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_attachments_message_id ON chat.attachments (message_id);

ALTER TABLE chat.mentions ENABLE ROW LEVEL SECURITY;
ALTER TABLE chat.mentions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON chat.mentions;
CREATE POLICY workspace_isolation ON chat.mentions USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

ALTER TABLE chat.message_reactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE chat.message_reactions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON chat.message_reactions;
CREATE POLICY workspace_isolation ON chat.message_reactions USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

ALTER TABLE chat.attachments ENABLE ROW LEVEL SECURITY;
ALTER TABLE chat.attachments FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON chat.attachments;
CREATE POLICY workspace_isolation ON chat.attachments USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
