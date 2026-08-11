-- Planvexa DbUp script 0017_AddChat.sql
-- Generated from EF Core migration 20260730075500_AddChat. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'chat') THEN
        CREATE SCHEMA chat;
    END IF;
END $$;

CREATE TABLE chat.channels (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    description character varying(1000),
    is_private boolean NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    archived_at_utc timestamp with time zone,
    CONSTRAINT pk_channels PRIMARY KEY (id),
    CONSTRAINT ak_channels_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE chat.messages (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    channel_id uuid NOT NULL,
    parent_message_id uuid,
    author_user_id uuid NOT NULL,
    body character varying(4000) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    edited_at_utc timestamp with time zone,
    is_deleted boolean NOT NULL,
    deleted_at_utc timestamp with time zone,
    CONSTRAINT pk_messages PRIMARY KEY (id)
);

CREATE TABLE chat.channel_members (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    channel_id uuid NOT NULL,
    user_id uuid NOT NULL,
    joined_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_channel_members PRIMARY KEY (id),
    CONSTRAINT fk_channel_members_channels_channel_id FOREIGN KEY (channel_id) REFERENCES chat.channels (id) ON DELETE CASCADE
);

CREATE INDEX ix_channel_members_channel_id ON chat.channel_members (channel_id);

CREATE UNIQUE INDEX ix_channel_members_tenant_id_channel_id_user_id ON chat.channel_members (tenant_id, channel_id, user_id);

CREATE INDEX ix_channels_tenant_id_workspace_id ON chat.channels (tenant_id, workspace_id);

CREATE INDEX ix_messages_tenant_id_channel_id_created_at_utc ON chat.messages (tenant_id, channel_id, created_at_utc);

CREATE INDEX ix_messages_tenant_id_parent_message_id ON chat.messages (tenant_id, parent_message_id);
