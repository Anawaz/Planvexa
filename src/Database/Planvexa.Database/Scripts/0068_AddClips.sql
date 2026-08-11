-- Planvexa DbUp script 0068_AddClips.sql
-- Clips, net new: a brand-new `clips` schema — clips.clips (uploaded/recorded video-audio file
-- metadata: title/privacy/owner/optional Task-or-Document link/storage path, mirrors Whiteboard.cs's
-- metadata shape; the media bytes live in IFileStorage, same abstraction WorkManagement's TaskAttachment
-- and Chat's ChatAttachment already use), clips.clip_comments (a lightweight dedicated comment thread,
-- same design choice and shape as goals.goal_comments from 0060_AddGoals.sql — see ClipComment.cs's doc
-- comment for why), clips.clip_transcripts (one per clip, full text + optional per-segment timestamps
-- JSON, see ClipTranscript.cs/IClipTranscriber's doc comments for the transcription investigation).
--
-- All CREATE ... IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9) — every table here is brand new with no existing rows to backfill. Every
-- table gets its own workspace_id NOT NULL + sole workspace_isolation RLS policy, the same pattern used by
-- every workspace-owned table since 0029/0030 (see 0060/0067's headers for the identical shape).

CREATE SCHEMA IF NOT EXISTS clips;

CREATE TABLE IF NOT EXISTS clips.clips (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    title character varying(300) NOT NULL,
    description character varying(2000),
    is_private boolean NOT NULL,
    owner_user_id uuid NOT NULL,
    linked_resource_type character varying(32),
    linked_resource_id uuid,
    storage_path character varying(1000) NOT NULL,
    content_type character varying(200) NOT NULL,
    size_bytes bigint NOT NULL,
    duration_seconds double precision,
    status character varying(24) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_clips PRIMARY KEY (id),
    CONSTRAINT fk_clips_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_clips_workspace_id ON clips.clips (workspace_id);
CREATE INDEX IF NOT EXISTS ix_clips_linked_resource ON clips.clips (linked_resource_type, linked_resource_id);

ALTER TABLE clips.clips ENABLE ROW LEVEL SECURITY;
ALTER TABLE clips.clips FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON clips.clips;
CREATE POLICY workspace_isolation ON clips.clips USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

CREATE TABLE IF NOT EXISTS clips.clip_comments (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    clip_id uuid NOT NULL,
    author_user_id uuid NOT NULL,
    body character varying(4000) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_clip_comments PRIMARY KEY (id),
    CONSTRAINT fk_clip_comments_clips_clip_id FOREIGN KEY (clip_id) REFERENCES clips.clips (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_clip_comments_workspace_id_clip_id_created_at_utc ON clips.clip_comments (workspace_id, clip_id, created_at_utc);

ALTER TABLE clips.clip_comments ENABLE ROW LEVEL SECURITY;
ALTER TABLE clips.clip_comments FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON clips.clip_comments;
CREATE POLICY workspace_isolation ON clips.clip_comments USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

CREATE TABLE IF NOT EXISTS clips.clip_transcripts (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    clip_id uuid NOT NULL,
    status character varying(24) NOT NULL,
    text text,
    segments_json text,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_clip_transcripts PRIMARY KEY (id),
    CONSTRAINT fk_clip_transcripts_clips_clip_id FOREIGN KEY (clip_id) REFERENCES clips.clips (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_clip_transcripts_clip_id ON clips.clip_transcripts (clip_id);
CREATE INDEX IF NOT EXISTS ix_clip_transcripts_workspace_id ON clips.clip_transcripts (workspace_id);

ALTER TABLE clips.clip_transcripts ENABLE ROW LEVEL SECURITY;
ALTER TABLE clips.clip_transcripts FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON clips.clip_transcripts;
CREATE POLICY workspace_isolation ON clips.clip_transcripts USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
