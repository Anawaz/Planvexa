-- Planvexa DbUp script 0074_AddBootstrapSecuritySettingsReadPolicy.sql
-- Bootstrap read policy for governance.enterprise_security_settings, same shape and reasoning as
-- 0020/0026: WorkspaceResolver.ResolveByWorkspaceIdAsync (0073's MFA enforcement) must read a
-- Workspace's MfaRequired flag BEFORE the ambient app.current_workspace is set -- that is the whole
-- point of resolution, deciding whether this request may proceed to set it at all. Without this,
-- 0029's sole workspace_isolation policy (which requires app.current_workspace to already be set)
-- returns zero rows here, and MfaRequired always reads as false regardless of the real setting.
--
-- Scoped to app.current_user (set by the connection interceptor for every authenticated request) via
-- the identical EXISTS-active-membership shape as bootstrap_entitlement_read/bootstrap_workspace_read
-- -- a caller can only bootstrap-read the security settings of a Workspace they are an active member
-- of. This is additive and SELECT-only; writes remain governed by the strict workspace_isolation
-- policy from 0029.

DROP POLICY IF EXISTS bootstrap_security_settings_read ON governance.enterprise_security_settings;

CREATE POLICY bootstrap_security_settings_read ON governance.enterprise_security_settings
FOR SELECT
USING (
    nullif(current_setting('app.current_user', true), '') IS NOT NULL
    AND EXISTS (
        SELECT 1
        FROM tenancy.workspace_members m
        WHERE m.workspace_id = enterprise_security_settings.workspace_id
          AND m.user_id = nullif(current_setting('app.current_user', true), '')::uuid
          AND m.status = 'Active'
    )
);
