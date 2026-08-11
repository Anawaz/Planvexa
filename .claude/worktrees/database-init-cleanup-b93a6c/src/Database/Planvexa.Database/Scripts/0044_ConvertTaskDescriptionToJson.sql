-- Planvexa DbUp script 0044_ConvertTaskDescriptionToJson.sql
-- Task management completeness: rich-text description storage shape.
--
-- work.tasks.description moves from a bare text column to jsonb, wrapping existing plain-text content as
-- a single-paragraph ProseMirror/Lexical-shaped doc ({"type":"doc","content":[{"type":"paragraph",
-- "content":[{"type":"text","text":"..."}]}]}) so the Lexical-based rich-text editor can consume it
-- without another migration. This change does NOT build a rich-text editor; the application still treats
-- Description as plain text (see DescriptionJson.cs), extracting the text content on read via an EF value
-- converter -- nothing else in the codebase needs to change.
--
-- Guarded by an information_schema check on the current column type so this is a no-op (and safe) if
-- re-run against an already-migrated database, and a no-op on an empty database (UPDATE affects zero
-- rows) per AGENTS.md rule 9.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'tasks' AND column_name = 'description'
              AND data_type <> 'jsonb'
    ) THEN
        ALTER TABLE work.tasks
            ALTER COLUMN description TYPE jsonb
            USING (
                CASE
                    WHEN description IS NULL OR description = '' THEN NULL
                    ELSE jsonb_build_object(
                        'type', 'doc',
                        'content', jsonb_build_array(
                            jsonb_build_object(
                                'type', 'paragraph',
                                'content', jsonb_build_array(
                                    jsonb_build_object('type', 'text', 'text', description)
                                )
                            )
                        )
                    )
                END
            );
    END IF;
END $$;
