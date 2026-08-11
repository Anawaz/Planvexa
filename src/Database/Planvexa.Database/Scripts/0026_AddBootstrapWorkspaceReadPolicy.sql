-- Bootstrap workspace read policy.
--
-- 0020 opened tenant/membership/entitlement reads before a tenant is resolved (keyed by
-- app.current_user). Presenting Workspace as the single top-level product concept (ADR 0015) needs
-- one more: listing every workspace the authenticated user belongs to, across internal tenants, from
-- GET /api/v1/workspaces/all — which runs with no ambient tenant. This additive SELECT-only policy
-- opens exactly that path, scoped to the current user's active memberships. Writes remain governed
-- by the strict tenant/workspace policies.

CREATE POLICY bootstrap_workspace_read ON tenancy.workspaces
FOR SELECT
USING (
    nullif(current_setting('app.current_user', true), '') IS NOT NULL
    AND EXISTS (
        SELECT 1
        FROM tenancy.workspace_members m
        WHERE m.workspace_id = workspaces.id
          AND m.user_id = nullif(current_setting('app.current_user', true), '')::uuid
          AND m.status = 'Active'
    )
);
