-- Planvexa DbUp script 0004_AddWorkManagementRls.sql
-- Generated from EF Core migration 20260729082809_AddWorkManagementRls. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

ALTER TABLE work.spaces ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.spaces FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.spaces
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.folders ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.folders FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.folders
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.lists ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.lists FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.lists
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.status_schemes ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.status_schemes FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.status_schemes
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.statuses ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.statuses FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.statuses
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.tasks ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.tasks FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.tasks
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.task_assignees ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.task_assignees FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.task_assignees
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.task_watchers ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.task_watchers FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.task_watchers
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.tags ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.tags FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.tags
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.task_tags ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.task_tags FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.task_tags
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.task_dependencies ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.task_dependencies FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.task_dependencies
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.task_checklists ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.task_checklists FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.task_checklists
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.task_checklist_items ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.task_checklist_items FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.task_checklist_items
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.custom_field_definitions ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.custom_field_definitions FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.custom_field_definitions
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.custom_field_options ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.custom_field_options FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.custom_field_options
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.custom_field_values ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.custom_field_values FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.custom_field_values
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.recurring_task_definitions ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.recurring_task_definitions FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.recurring_task_definitions
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.recurring_occurrences ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.recurring_occurrences FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.recurring_occurrences
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.task_activity_events ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.task_activity_events FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.task_activity_events
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE work.saved_views ENABLE ROW LEVEL SECURITY;

ALTER TABLE work.saved_views FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON work.saved_views
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
