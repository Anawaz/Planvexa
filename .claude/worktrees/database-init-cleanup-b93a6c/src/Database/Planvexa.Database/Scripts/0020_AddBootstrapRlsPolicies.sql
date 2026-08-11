-- Bootstrap read policies.
--
-- 0019 hardened tenant RLS so a missing app.current_tenant yields no rows. That is correct for
-- tenant-scoped work, but three reads legitimately happen BEFORE a workspace is resolved, keyed by
-- the authenticated user instead (see TenantResolver):
--   * listing the user's own memberships,
--   * loading the workspaces those memberships point at,
--   * loading feature entitlements while resolving workspace context.
-- These additive SELECT-only policies open exactly those paths, scoped to app.current_user, which
-- the connection interceptor sets from the authenticated application user. Writes remain governed
-- by the strict tenant/workspace policies from 0019.

CREATE POLICY bootstrap_member_read ON tenancy.workspace_members
FOR SELECT
USING (
    nullif(current_setting('app.current_user', true), '') IS NOT NULL
    AND user_id = nullif(current_setting('app.current_user', true), '')::uuid
);

CREATE POLICY bootstrap_tenant_read ON tenancy.tenants
FOR SELECT
USING (
    nullif(current_setting('app.current_user', true), '') IS NOT NULL
    AND EXISTS (
        SELECT 1
        FROM tenancy.workspace_members m
        WHERE m.tenant_id = tenants.id
          AND m.user_id = nullif(current_setting('app.current_user', true), '')::uuid
          AND m.status = 'Active'
    )
);

CREATE POLICY bootstrap_entitlement_read ON tenancy.feature_entitlements
FOR SELECT
USING (
    nullif(current_setting('app.current_user', true), '') IS NOT NULL
    AND EXISTS (
        SELECT 1
        FROM tenancy.workspace_members m
        WHERE m.workspace_id = feature_entitlements.workspace_id
          AND m.user_id = nullif(current_setting('app.current_user', true), '')::uuid
          AND m.status = 'Active'
    )
);
