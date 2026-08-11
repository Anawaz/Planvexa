-- Planvexa DbUp script 0018_AddChatRls.sql
-- Generated from EF Core migration 20260730075533_AddChatRls. EF migration history writes removed; DbUp journals this script in platform.schema_versions.

ALTER TABLE chat.channels ENABLE ROW LEVEL SECURITY;

ALTER TABLE chat.channels FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON chat.channels
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE chat.channel_members ENABLE ROW LEVEL SECURITY;

ALTER TABLE chat.channel_members FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON chat.channel_members
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);

ALTER TABLE chat.messages ENABLE ROW LEVEL SECURITY;

ALTER TABLE chat.messages FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON chat.messages
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NULL
    OR tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
