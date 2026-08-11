-- Planvexa DbUp script 0046_AddCustomFieldFormulaAndRollup.sql
-- Custom fields completeness: Formula and Rollup field types.
--
-- work.custom_field_definitions gains:
--   formula_expression        -- the "{FieldA} + {FieldB}"-style expression (Formula fields only).
--   formula_dependency_ids    -- comma-separated definition ids the expression's {FieldName} refs resolved
--                                 to at save time, used for cycle detection and read-time evaluation
--                                 ordering (see CustomFieldDependencyGraph). Plain text, not jsonb -- it is
--                                 always read as a whole, never queried into.
--   rollup_source_type        -- 'Subtasks' or 'RelationshipField' (Rollup fields only).
--   rollup_source_field_id    -- the Relationship-type field id, when source is RelationshipField.
--   rollup_target_field_id    -- the field to aggregate (null only when the function is Count).
--   rollup_function           -- 'Sum' | 'Count' | 'Average' | 'Min' | 'Max'.
--
-- No FK constraints on rollup_source_field_id/rollup_target_field_id: they reference sibling rows in the
-- SAME table (custom_field_definitions), and Postgres self-referencing FKs on a nullable column added via
-- ALTER are unnecessary ceremony here -- the application validates existence/type at save time
-- (CustomFieldService), consistent with how rollup_source_field_id/target are opaque ids elsewhere in this
-- module (e.g. task_team_assignees.team_id).
--
-- DO-block column guards: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9).

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'custom_field_definitions' AND column_name = 'formula_expression'
    ) THEN
        ALTER TABLE work.custom_field_definitions ADD COLUMN formula_expression character varying(2000) NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'custom_field_definitions' AND column_name = 'formula_dependency_ids'
    ) THEN
        ALTER TABLE work.custom_field_definitions ADD COLUMN formula_dependency_ids character varying(4000) NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'custom_field_definitions' AND column_name = 'rollup_source_type'
    ) THEN
        ALTER TABLE work.custom_field_definitions ADD COLUMN rollup_source_type character varying(24) NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'custom_field_definitions' AND column_name = 'rollup_source_field_id'
    ) THEN
        ALTER TABLE work.custom_field_definitions ADD COLUMN rollup_source_field_id uuid NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'custom_field_definitions' AND column_name = 'rollup_target_field_id'
    ) THEN
        ALTER TABLE work.custom_field_definitions ADD COLUMN rollup_target_field_id uuid NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'work' AND table_name = 'custom_field_definitions' AND column_name = 'rollup_function'
    ) THEN
        ALTER TABLE work.custom_field_definitions ADD COLUMN rollup_function character varying(16) NULL;
    END IF;
END $$;
