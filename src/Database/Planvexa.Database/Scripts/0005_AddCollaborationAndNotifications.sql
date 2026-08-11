-- Planvexa DbUp script 0005_AddCollaborationAndNotifications.sql
-- Generated from EF Core migration 20260729085855_AddCollaborationAndNotifications. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'collab') THEN
        CREATE SCHEMA collab;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'notifications') THEN
        CREATE SCHEMA notifications;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'sharing') THEN
        CREATE SCHEMA sharing;
    END IF;
END $$;

CREATE TABLE collab.comments (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    parent_id uuid,
    author_user_id uuid NOT NULL,
    body character varying(10000) NOT NULL,
    is_edited boolean NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone,
    is_deleted boolean NOT NULL,
    deleted_at_utc timestamp with time zone,
    deleted_by_user_id uuid,
    CONSTRAINT pk_comments PRIMARY KEY (id),
    CONSTRAINT ak_comments_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE notifications.notification_preferences (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    event_type character varying(64) NOT NULL,
    inbox boolean NOT NULL,
    email boolean NOT NULL,
    CONSTRAINT pk_notification_preferences PRIMARY KEY (id)
);

CREATE TABLE notifications.notifications (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    recipient_user_id uuid NOT NULL,
    event_type character varying(64) NOT NULL,
    entity_type character varying(64) NOT NULL,
    entity_id uuid NOT NULL,
    payload jsonb,
    deduplication_key character varying(200) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    read_at_utc timestamp with time zone,
    CONSTRAINT pk_notifications PRIMARY KEY (id),
    CONSTRAINT ak_notifications_tenant_id_id UNIQUE (tenant_id, id)
);

CREATE TABLE sharing.share_links (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    token_hash character varying(128) NOT NULL,
    created_by_user_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    expires_at_utc timestamp with time zone,
    is_revoked boolean NOT NULL,
    CONSTRAINT pk_share_links PRIMARY KEY (id)
);

CREATE TABLE collab.comment_reactions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    comment_id uuid NOT NULL,
    user_id uuid NOT NULL,
    emoji character varying(32) NOT NULL,
    CONSTRAINT pk_comment_reactions PRIMARY KEY (id),
    CONSTRAINT fk_comment_reactions_comments_comment_id FOREIGN KEY (comment_id) REFERENCES collab.comments (id) ON DELETE CASCADE
);

CREATE TABLE collab.mentions (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    comment_id uuid NOT NULL,
    task_id uuid NOT NULL,
    mentioned_user_id uuid NOT NULL,
    CONSTRAINT pk_mentions PRIMARY KEY (id),
    CONSTRAINT fk_mentions_comments_comment_id FOREIGN KEY (comment_id) REFERENCES collab.comments (id) ON DELETE CASCADE
);

CREATE TABLE notifications.notification_deliveries (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    notification_id uuid NOT NULL,
    channel character varying(16) NOT NULL,
    status character varying(16) NOT NULL,
    attempts integer NOT NULL,
    error character varying(2048),
    created_at_utc timestamp with time zone NOT NULL,
    sent_at_utc timestamp with time zone,
    CONSTRAINT pk_notification_deliveries PRIMARY KEY (id),
    CONSTRAINT fk_notification_deliveries_notifications_notification_id FOREIGN KEY (notification_id) REFERENCES notifications.notifications (id) ON DELETE CASCADE
);

CREATE INDEX ix_comment_reactions_comment_id ON collab.comment_reactions (comment_id);

CREATE UNIQUE INDEX ix_comment_reactions_tenant_id_comment_id_user_id_emoji ON collab.comment_reactions (tenant_id, comment_id, user_id, emoji);

CREATE INDEX ix_comments_tenant_id_parent_id ON collab.comments (tenant_id, parent_id);

CREATE INDEX ix_comments_tenant_id_task_id_created_at_utc ON collab.comments (tenant_id, task_id, created_at_utc);

CREATE INDEX ix_mentions_comment_id ON collab.mentions (comment_id);

CREATE UNIQUE INDEX ix_mentions_tenant_id_comment_id_mentioned_user_id ON collab.mentions (tenant_id, comment_id, mentioned_user_id);

CREATE INDEX ix_mentions_tenant_id_mentioned_user_id ON collab.mentions (tenant_id, mentioned_user_id);

CREATE INDEX ix_notification_deliveries_notification_id ON notifications.notification_deliveries (notification_id);

CREATE INDEX ix_notification_deliveries_status_created_at_utc ON notifications.notification_deliveries (status, created_at_utc);

CREATE UNIQUE INDEX ix_notification_preferences_tenant_id_user_id_event_type ON notifications.notification_preferences (tenant_id, user_id, event_type);

CREATE INDEX ix_notifications_tenant_id_recipient_user_id_created_at_utc ON notifications.notifications (tenant_id, recipient_user_id, created_at_utc);

CREATE UNIQUE INDEX ix_notifications_tenant_id_recipient_user_id_deduplication_key ON notifications.notifications (tenant_id, recipient_user_id, deduplication_key);

CREATE INDEX ix_notifications_tenant_id_recipient_user_id_read_at_utc ON notifications.notifications (tenant_id, recipient_user_id, read_at_utc);

CREATE INDEX ix_share_links_tenant_id_task_id ON sharing.share_links (tenant_id, task_id);

CREATE UNIQUE INDEX ix_share_links_token_hash ON sharing.share_links (token_hash);
