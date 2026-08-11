-- Planvexa DbUp script 0059_AddTimeTrackingTagsBudgetsReminders.sql
-- Time tracking polish: free-form tags on time entries (time.time_tags +
-- time.time_entry_tags, a lightweight tag list owned by TimeTracking itself rather than reusing
-- WorkManagement's task Tag -- see TimeEntry.SetTags doc comment for why), Space/List-scoped budgets
-- with a monetary and/or time cap for profitability reporting (time.budgets), and missing-time
-- reminder settings on time.time_policies (per-workspace threshold + cadence, delivered by the
-- MissingTimeReminderBackgroundService via INotificationPublisher). All ADD COLUMN/CREATE ... IF NOT
-- EXISTS: safe on both an empty database and the current already-migrated dev database (AGENTS.md
-- rule 9) -- new columns are nullable/defaulted settings with no data to backfill, and the new tables
-- are brand-new with no existing rows. Every new table gets its own workspace_id NOT NULL + sole
-- workspace_isolation RLS policy, the same pattern used by every workspace-owned table since 0029/0030
-- (see 0053's header).

ALTER TABLE time.time_policies ADD COLUMN IF NOT EXISTS missing_time_reminder_enabled boolean NOT NULL DEFAULT false;
ALTER TABLE time.time_policies ADD COLUMN IF NOT EXISTS missing_time_reminder_cadence character varying(16) NOT NULL DEFAULT 'Daily';
ALTER TABLE time.time_policies ADD COLUMN IF NOT EXISTS missing_time_reminder_minimum_seconds bigint NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS time.time_tags (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_time_tags PRIMARY KEY (id),
    CONSTRAINT fk_time_tags_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

-- Case-insensitive uniqueness per workspace (matches how the service looks up an existing tag by name).
CREATE UNIQUE INDEX IF NOT EXISTS ux_time_tags_workspace_id_name ON time.time_tags (workspace_id, lower(name));

ALTER TABLE time.time_tags ENABLE ROW LEVEL SECURITY;
ALTER TABLE time.time_tags FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON time.time_tags;
CREATE POLICY workspace_isolation ON time.time_tags USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

CREATE TABLE IF NOT EXISTS time.time_entry_tags (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    time_entry_id uuid NOT NULL,
    tag_id uuid NOT NULL,
    CONSTRAINT pk_time_entry_tags PRIMARY KEY (id),
    CONSTRAINT fk_time_entry_tags_time_entries_time_entry_id FOREIGN KEY (time_entry_id) REFERENCES time.time_entries (id) ON DELETE CASCADE,
    CONSTRAINT fk_time_entry_tags_time_tags_tag_id FOREIGN KEY (tag_id) REFERENCES time.time_tags (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_time_entry_tags_entry_tag ON time.time_entry_tags (time_entry_id, tag_id);
CREATE INDEX IF NOT EXISTS ix_time_entry_tags_workspace_id_tag_id ON time.time_entry_tags (workspace_id, tag_id);

ALTER TABLE time.time_entry_tags ENABLE ROW LEVEL SECURITY;
ALTER TABLE time.time_entry_tags FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON time.time_entry_tags;
CREATE POLICY workspace_isolation ON time.time_entry_tags USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);

-- scope_type is 'Space' or 'List' (a List is what this codebase's time reports already call a
-- "project" -- see TimeReportService.GroupByListAsync / MemberRate.ProjectId). scope_id is the
-- Space.Id or TaskList.Id resolved through WorkManagement's ITaskDirectory (TaskRef.SpaceId /
-- TaskRef.ListId) -- TimeTracking does not have a foreign key into WorkManagement's tables
-- (AGENTS.md rule 7: modules integrate through contracts, not shared tables).
CREATE TABLE IF NOT EXISTS time.budgets (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    scope_type character varying(16) NOT NULL,
    scope_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    monetary_cap_amount numeric(18,4),
    time_cap_seconds bigint,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone,
    CONSTRAINT pk_budgets PRIMARY KEY (id),
    CONSTRAINT fk_budgets_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_budgets_workspace_id_scope ON time.budgets (workspace_id, scope_type, scope_id);

ALTER TABLE time.budgets ENABLE ROW LEVEL SECURITY;
ALTER TABLE time.budgets FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS workspace_isolation ON time.budgets;
CREATE POLICY workspace_isolation ON time.budgets USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
