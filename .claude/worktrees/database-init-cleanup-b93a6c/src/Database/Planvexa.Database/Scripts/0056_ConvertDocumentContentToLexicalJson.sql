-- Planvexa DbUp script 0056_ConvertDocumentContentToLexicalJson.sql
-- Documents & Wikis, item 2: docs.documents.content and docs.document_versions.content move from
-- bare plain text to a serialized Lexical editor-state JSON string (see LexicalJson.cs for the exact
-- schema), so the new Lexical-based rich-text editor can consume existing content without a data-loss
-- migration. The column TYPE stays `text` (see DocumentsConfigurations.cs -- the app treats it as an
-- opaque string it never queries into, so jsonb + a value converter would only add re-encoding cost with
-- no benefit); this script rewrites the VALUE of every existing row to wrap the old plain text as a
-- single-paragraph Lexical doc, using jsonb_build_object (cast to text) so the old text is safely
-- JSON-escaped without hand-rolled string escaping.
--
-- Guarded so this is a no-op (and safe) if re-run against an already-migrated database (rows already
-- starting with the Lexical root marker are skipped) and a no-op on an empty database, per AGENTS.md rule 9.

UPDATE docs.documents
SET content = (
    jsonb_build_object(
        'root', jsonb_build_object(
            'children', jsonb_build_array(
                jsonb_build_object(
                    'children', CASE WHEN content = '' THEN jsonb_build_array() ELSE jsonb_build_array(
                        jsonb_build_object(
                            'detail', 0, 'format', 0, 'mode', 'normal', 'style', '',
                            'text', content, 'type', 'text', 'version', 1
                        )
                    ) END,
                    'direction', 'ltr', 'format', '', 'indent', 0, 'type', 'paragraph', 'version', 1
                )
            ),
            'direction', 'ltr', 'format', '', 'indent', 0, 'type', 'root', 'version', 1
        )
    )
)::text
WHERE content IS NOT NULL AND content NOT LIKE '{"root":%';

UPDATE docs.document_versions
SET content = (
    jsonb_build_object(
        'root', jsonb_build_object(
            'children', jsonb_build_array(
                jsonb_build_object(
                    'children', CASE WHEN content = '' THEN jsonb_build_array() ELSE jsonb_build_array(
                        jsonb_build_object(
                            'detail', 0, 'format', 0, 'mode', 'normal', 'style', '',
                            'text', content, 'type', 'text', 'version', 1
                        )
                    ) END,
                    'direction', 'ltr', 'format', '', 'indent', 0, 'type', 'paragraph', 'version', 1
                )
            ),
            'direction', 'ltr', 'format', '', 'indent', 0, 'type', 'root', 'version', 1
        )
    )
)::text
WHERE content IS NOT NULL AND content NOT LIKE '{"root":%';
