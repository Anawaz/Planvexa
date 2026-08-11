-- Planvexa DbUp script 0052_AddChatChannelTypesAndLinking.sql
-- Chat overhaul: introduces ChatChannelType as the channel discriminator (Workspace=0,
-- Space=1, List=2, Task=3, Private=4, Dm=5, GroupDm=6 — see Planvexa.Modules.Chat.Domain.ChatChannelType)
-- and Space/List/Task channel linking (linked_resource_type/linked_resource_id). channel_type is stored
-- as `integer` (not smallint) to match EF's default enum-to-int mapping without a value converter.
--
-- is_private is KEPT (not replaced) — every existing row's access rule stays exactly as it was, and
-- ChatChannel.CanBeAccessedBy still reads it directly; channel_type only adds the discriminator plus the
-- new linked-resource ACL gate layered on top by ChatChannelService (see that class's CanAccessAsync doc
-- comment). Backfill: every row that already exists predates this column and was created through the
-- original Create() factory, which only ever produced Workspace (is_private=false) or Private
-- (is_private=true) channels — so channel_type is fully derivable from is_private for the "upgraded DB"
-- half of AGENTS.md rule 9. New rows default to Workspace (0) and are set explicitly by the application.
--
-- Safe on both an empty database and the current already-migrated dev database: IF NOT EXISTS / DO-block
-- guards throughout.

ALTER TABLE chat.channels ADD COLUMN IF NOT EXISTS channel_type integer NOT NULL DEFAULT 0;
ALTER TABLE chat.channels ADD COLUMN IF NOT EXISTS linked_resource_type character varying(32);
ALTER TABLE chat.channels ADD COLUMN IF NOT EXISTS linked_resource_id uuid;

-- Upgraded-DB backfill: existing private channels become ChannelType.Private (4); everything else stays
-- Workspace (0), which is already the column default.
UPDATE chat.channels SET channel_type = 4 WHERE is_private = true AND channel_type = 0;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_channels_channel_type') THEN
        ALTER TABLE chat.channels ADD CONSTRAINT ck_channels_channel_type CHECK (channel_type BETWEEN 0 AND 6);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_channels_linked_resource') THEN
        ALTER TABLE chat.channels ADD CONSTRAINT ck_channels_linked_resource
            CHECK ((linked_resource_type IS NULL) = (linked_resource_id IS NULL));
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_channels_linked_resource_type_linked_resource_id
    ON chat.channels (linked_resource_type, linked_resource_id);
