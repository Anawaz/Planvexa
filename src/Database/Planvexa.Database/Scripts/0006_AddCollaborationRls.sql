-- Planvexa DbUp script 0006_AddCollaborationRls.sql
-- Generated from EF Core migration 20260729085913_AddCollaborationRls. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

ALTER TABLE collab.comments ENABLE ROW LEVEL SECURITY;

ALTER TABLE collab.comments FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON collab.comments
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE collab.mentions ENABLE ROW LEVEL SECURITY;

ALTER TABLE collab.mentions FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON collab.mentions
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE collab.comment_reactions ENABLE ROW LEVEL SECURITY;

ALTER TABLE collab.comment_reactions FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON collab.comment_reactions
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE sharing.share_links ENABLE ROW LEVEL SECURITY;

ALTER TABLE sharing.share_links FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON sharing.share_links
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE notifications.notifications ENABLE ROW LEVEL SECURITY;

ALTER TABLE notifications.notifications FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON notifications.notifications
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE notifications.notification_deliveries ENABLE ROW LEVEL SECURITY;

ALTER TABLE notifications.notification_deliveries FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON notifications.notification_deliveries
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE notifications.notification_preferences ENABLE ROW LEVEL SECURITY;

ALTER TABLE notifications.notification_preferences FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON notifications.notification_preferences
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
