-- Planvexa DbUp script 0092_AddWorkspaceCascadeForeignKeys.sql
--
-- Deleting a Workspace has to remove everything the Workspace owns. 97 tables carry a workspace_id
-- column but only 38 of them ever got a FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces
-- ON DELETE CASCADE, so a plain DELETE FROM tenancy.workspaces left ~60 tables' rows orphaned.
-- Closing the gap in the database (rather than hand-curating a ~90-table delete order in C#) means
-- WorkspaceDeletionService is a single DELETE statement and every future workspace-owned table
-- inherits the behaviour by declaring its own FK the way the existing 38 do.
--
-- NOT VALID is deliberate: it skips the validating scan of existing rows, so an already-migrated
-- database that holds pre-existing orphans (a workspace_id pointing at a workspace that is already
-- gone) still migrates instead of failing. It does NOT weaken the delete: the referential-integrity
-- triggers are created exactly as for a validated constraint, so ON DELETE CASCADE fires normally
-- for every row from here on. New rows are checked as usual.
--
-- DEFERRABLE INITIALLY DEFERRED is deliberate too: these tables have no matching relationship in the
-- EF model, so EF does not know the workspace row must be INSERTed before its children and will
-- happily order a batch the other way round (workspace onboarding writes the workspace and its
-- starter space/list/roles in one SaveChanges). Deferring the check to COMMIT makes intra-transaction
-- ordering irrelevant while still enforcing the constraint, and ON DELETE CASCADE fires as normal.
--
-- Excluded on purpose:
--   * tenancy.workspaces      -- owns itself; its workspace_id IS its id, an FK would be circular.
--   * audit.audit_events      -- nullable, platform-scoped: audit history must OUTLIVE the workspace
--                                it describes (workspace.deleted is written just before the delete).
--   * platform.outbox_messages -- nullable, platform-scoped; WorkspaceDeletionService clears the
--                                deleted workspace's rows explicitly instead.
--
-- Idempotent: a table that already has a workspace_id foreign key to tenancy.workspaces is skipped,
-- which on a re-run includes every constraint this script itself added (AGENTS.md rule 9).

DO $$
DECLARE
    target record;
    constraint_name text;
BEGIN
    FOR target IN
        SELECT c.table_schema, c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
        WHERE c.column_name = 'workspace_id'
          AND c.data_type = 'uuid'
          AND c.table_schema NOT IN ('pg_catalog', 'information_schema')
          AND (c.table_schema, c.table_name) NOT IN (
                ('tenancy', 'workspaces'),
                ('audit', 'audit_events'),
                ('platform', 'outbox_messages'))
          AND NOT EXISTS (
                SELECT 1
                FROM pg_constraint fk
                JOIN pg_attribute a ON a.attrelid = fk.conrelid AND a.attnum = ANY (fk.conkey)
                WHERE fk.contype = 'f'
                  AND fk.conrelid = format('%I.%I', c.table_schema, c.table_name)::regclass
                  AND fk.confrelid = 'tenancy.workspaces'::regclass
                  AND a.attname = 'workspace_id')
        ORDER BY c.table_schema, c.table_name
    LOOP
        -- Same fk_{table}_workspaces_workspace_id shape the 38 hand-written ones already use (see
        -- 0030/0031/0034). Constraint names only need to be unique per table, so the table name alone
        -- is deterministic; left() keeps it inside Postgres's 63-character identifier limit.
        constraint_name := left(format('fk_%s_workspaces_workspace_id', target.table_name), 63);
        EXECUTE format(
            'ALTER TABLE %I.%I ADD CONSTRAINT %I FOREIGN KEY (workspace_id) '
            || 'REFERENCES tenancy.workspaces (id) ON DELETE CASCADE '
            || 'DEFERRABLE INITIALLY DEFERRED NOT VALID',
            target.table_schema, target.table_name, constraint_name);
    END LOOP;
END $$;

-- The cascade above is useless without being able to delete the workspace row itself, and
-- tenancy.workspaces is the one workspace-owned table 0029 did NOT give a workspace_isolation policy
-- to: all it has left is bootstrap_workspace_read (FOR SELECT, 0026) and bootstrap_workspace_write
-- (FOR INSERT, 0029). With FORCE ROW LEVEL SECURITY on and no policy covering DELETE, every DELETE
-- was silently filtered to zero rows. Add the same ambient-workspace match every other table uses —
-- a workspace's workspace_id IS its own id, so this allows deleting exactly the workspace the caller
-- is currently inside, and nothing else. DROP ... IF EXISTS keeps the script idempotent.
DROP POLICY IF EXISTS workspace_self_delete ON tenancy.workspaces;
CREATE POLICY workspace_self_delete ON tenancy.workspaces
FOR DELETE
USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
