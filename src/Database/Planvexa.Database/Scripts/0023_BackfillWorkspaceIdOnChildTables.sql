-- Planvexa DbUp script 0023_BackfillWorkspaceIdOnChildTables.sql
-- ADR 0015: expand phase of the Tenant->Workspace migration.
--
-- Adds a nullable workspace_id to workspace-owned CHILD tables that currently scope only via
-- tenant_id + a parent, and backfills each from its parent (whose workspace_id is already NOT NULL).
-- Columns are added NULLABLE and are not yet mapped by EF, so existing inserts are unaffected. The
-- NOT NULL constraint, save-stamping, and Workspace RLS are applied in later coupled tasks
-- tenant_id is retained until the final removal task.
--
-- Idempotent: ADD COLUMN IF NOT EXISTS + CREATE INDEX IF NOT EXISTS + re-runnable backfills.

-- ---------------------------------------------------------------------------------------------
-- work schema
-- ---------------------------------------------------------------------------------------------
ALTER TABLE work.task_checklists       ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE work.task_dependencies     ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE work.task_checklist_items  ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE work.custom_field_options  ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE work.custom_field_values   ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE work.recurring_occurrences ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE work.statuses              ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE work.task_assignees        ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE work.task_tags             ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE work.task_watchers         ADD COLUMN IF NOT EXISTS workspace_id uuid;

UPDATE work.task_dependencies c     SET workspace_id = p.workspace_id FROM work.tasks p                     WHERE c.task_id = p.id       AND c.workspace_id IS NULL;
UPDATE work.custom_field_values c   SET workspace_id = p.workspace_id FROM work.tasks p                     WHERE c.task_id = p.id       AND c.workspace_id IS NULL;
UPDATE work.task_assignees c        SET workspace_id = p.workspace_id FROM work.tasks p                     WHERE c.task_id = p.id       AND c.workspace_id IS NULL;
UPDATE work.task_tags c             SET workspace_id = p.workspace_id FROM work.tasks p                     WHERE c.task_id = p.id       AND c.workspace_id IS NULL;
UPDATE work.task_watchers c         SET workspace_id = p.workspace_id FROM work.tasks p                     WHERE c.task_id = p.id       AND c.workspace_id IS NULL;
UPDATE work.task_checklists c       SET workspace_id = p.workspace_id FROM work.tasks p                     WHERE c.task_id = p.id       AND c.workspace_id IS NULL;
UPDATE work.custom_field_options c  SET workspace_id = p.workspace_id FROM work.custom_field_definitions p  WHERE c.definition_id = p.id AND c.workspace_id IS NULL;
UPDATE work.recurring_occurrences c SET workspace_id = p.workspace_id FROM work.recurring_task_definitions p WHERE c.definition_id = p.id AND c.workspace_id IS NULL;
UPDATE work.statuses c              SET workspace_id = p.workspace_id FROM work.status_schemes p            WHERE c.scheme_id = p.id     AND c.workspace_id IS NULL;
-- checklist_items resolve through task_checklists, so run after that table is backfilled.
UPDATE work.task_checklist_items c  SET workspace_id = p.workspace_id FROM work.task_checklists p          WHERE c.checklist_id = p.id  AND c.workspace_id IS NULL;

CREATE INDEX IF NOT EXISTS ix_task_checklists_workspace_id       ON work.task_checklists (workspace_id);
CREATE INDEX IF NOT EXISTS ix_task_dependencies_workspace_id     ON work.task_dependencies (workspace_id);
CREATE INDEX IF NOT EXISTS ix_task_checklist_items_workspace_id  ON work.task_checklist_items (workspace_id);
CREATE INDEX IF NOT EXISTS ix_custom_field_options_workspace_id  ON work.custom_field_options (workspace_id);
CREATE INDEX IF NOT EXISTS ix_custom_field_values_workspace_id   ON work.custom_field_values (workspace_id);
CREATE INDEX IF NOT EXISTS ix_recurring_occurrences_workspace_id ON work.recurring_occurrences (workspace_id);
CREATE INDEX IF NOT EXISTS ix_statuses_workspace_id              ON work.statuses (workspace_id);
CREATE INDEX IF NOT EXISTS ix_task_assignees_workspace_id        ON work.task_assignees (workspace_id);
CREATE INDEX IF NOT EXISTS ix_task_tags_workspace_id             ON work.task_tags (workspace_id);
CREATE INDEX IF NOT EXISTS ix_task_watchers_workspace_id         ON work.task_watchers (workspace_id);

-- ---------------------------------------------------------------------------------------------
-- collab / notifications schema
-- ---------------------------------------------------------------------------------------------
ALTER TABLE collab.comment_reactions          ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE collab.mentions                   ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE notifications.notification_deliveries ADD COLUMN IF NOT EXISTS workspace_id uuid;

UPDATE collab.comment_reactions c SET workspace_id = p.workspace_id FROM collab.comments p       WHERE c.comment_id = p.id      AND c.workspace_id IS NULL;
UPDATE collab.mentions c          SET workspace_id = p.workspace_id FROM collab.comments p       WHERE c.comment_id = p.id      AND c.workspace_id IS NULL;
UPDATE notifications.notification_deliveries c SET workspace_id = p.workspace_id FROM notifications.notifications p WHERE c.notification_id = p.id AND c.workspace_id IS NULL;

CREATE INDEX IF NOT EXISTS ix_comment_reactions_workspace_id       ON collab.comment_reactions (workspace_id);
CREATE INDEX IF NOT EXISTS ix_mentions_workspace_id                ON collab.mentions (workspace_id);
CREATE INDEX IF NOT EXISTS ix_notification_deliveries_workspace_id ON notifications.notification_deliveries (workspace_id);

-- ---------------------------------------------------------------------------------------------
-- time schema
-- ---------------------------------------------------------------------------------------------
ALTER TABLE time.time_entry_audits    ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE time.timesheet_approvals  ADD COLUMN IF NOT EXISTS workspace_id uuid;

UPDATE time.time_entry_audits c   SET workspace_id = p.workspace_id FROM time.time_entries p      WHERE c.time_entry_id = p.id AND c.workspace_id IS NULL;
UPDATE time.timesheet_approvals c SET workspace_id = p.workspace_id FROM time.timesheet_periods p WHERE c.period_id = p.id     AND c.workspace_id IS NULL;

CREATE INDEX IF NOT EXISTS ix_time_entry_audits_workspace_id   ON time.time_entry_audits (workspace_id);
CREATE INDEX IF NOT EXISTS ix_timesheet_approvals_workspace_id ON time.timesheet_approvals (workspace_id);

-- ---------------------------------------------------------------------------------------------
-- planning / reporting schema
-- ---------------------------------------------------------------------------------------------
ALTER TABLE reporting.dashboard_widgets ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE planning.sprint_items       ADD COLUMN IF NOT EXISTS workspace_id uuid;

UPDATE reporting.dashboard_widgets c SET workspace_id = p.workspace_id FROM reporting.dashboards p WHERE c.dashboard_id = p.id AND c.workspace_id IS NULL;
UPDATE planning.sprint_items c       SET workspace_id = p.workspace_id FROM planning.sprints p     WHERE c.sprint_id = p.id    AND c.workspace_id IS NULL;

CREATE INDEX IF NOT EXISTS ix_dashboard_widgets_workspace_id ON reporting.dashboard_widgets (workspace_id);
CREATE INDEX IF NOT EXISTS ix_sprint_items_workspace_id      ON planning.sprint_items (workspace_id);

-- ---------------------------------------------------------------------------------------------
-- docs / forms schema
-- ---------------------------------------------------------------------------------------------
ALTER TABLE docs.document_versions ADD COLUMN IF NOT EXISTS workspace_id uuid;
ALTER TABLE forms.form_fields      ADD COLUMN IF NOT EXISTS workspace_id uuid;

UPDATE docs.document_versions c SET workspace_id = p.workspace_id FROM docs.documents p WHERE c.document_id = p.id AND c.workspace_id IS NULL;
UPDATE forms.form_fields c      SET workspace_id = p.workspace_id FROM forms.forms p     WHERE c.form_id = p.id     AND c.workspace_id IS NULL;

CREATE INDEX IF NOT EXISTS ix_document_versions_workspace_id ON docs.document_versions (workspace_id);
CREATE INDEX IF NOT EXISTS ix_form_fields_workspace_id       ON forms.form_fields (workspace_id);

-- ---------------------------------------------------------------------------------------------
-- billing schema
-- ---------------------------------------------------------------------------------------------
-- billing.invoice_lines is intentionally deferred: its parent (billing.invoices) is still
-- tenant-level and has no workspace_id. Backfilling it requires the billing workspace-ownership
-- migration (attach subscription/invoices to the appropriate Workspace).

-- ---------------------------------------------------------------------------------------------
-- chat schema
-- ---------------------------------------------------------------------------------------------
ALTER TABLE chat.channel_members ADD COLUMN IF NOT EXISTS workspace_id uuid;

UPDATE chat.channel_members c SET workspace_id = p.workspace_id FROM chat.channels p WHERE c.channel_id = p.id AND c.workspace_id IS NULL;

CREATE INDEX IF NOT EXISTS ix_channel_members_workspace_id ON chat.channel_members (workspace_id);

-- ---------------------------------------------------------------------------------------------
-- Validation guard: every child row backfilled from a workspace-owned parent must now be mapped.
-- Fails the migration (and its DbUp test) if any parent-derived row remains unmapped, proving the
-- backfill is complete on both blank and upgraded databases. billing.invoice_lines is excluded
-- because its parent is not yet workspace-owned (see note above).
-- ---------------------------------------------------------------------------------------------
DO $$
DECLARE
    unmapped bigint;
BEGIN
    SELECT
        (SELECT count(*) FROM work.task_checklists       WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM work.task_dependencies     WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM work.task_checklist_items  WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM work.custom_field_options  WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM work.custom_field_values   WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM work.recurring_occurrences WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM work.statuses              WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM work.task_assignees        WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM work.task_tags             WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM work.task_watchers         WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM collab.comment_reactions   WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM collab.mentions            WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM notifications.notification_deliveries WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM time.time_entry_audits     WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM time.timesheet_approvals   WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM reporting.dashboard_widgets WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM planning.sprint_items      WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM docs.document_versions     WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM forms.form_fields          WHERE workspace_id IS NULL)
      + (SELECT count(*) FROM chat.channel_members       WHERE workspace_id IS NULL)
    INTO unmapped;

    IF unmapped > 0 THEN
        RAISE EXCEPTION 'Workspace backfill incomplete: % child row(s) still have a NULL workspace_id after backfill.', unmapped;
    END IF;
END $$;
