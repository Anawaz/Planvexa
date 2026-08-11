-- Planvexa DbUp script 0051_AddShareLinkPermissionLevelAndPublicComments.sql
-- Collaboration polish: public-link hardening — granular permission restriction (View vs
-- View+Comment; Edit and above always stays internal-only, see PublicShareLink.AllowedLevels) and a
-- separate guest-comment record for Comment-level links (never the internal collab.comments aggregate,
-- which requires a real workspace-member author — see PublicComment's doc comment).
--
-- permission_level stores Planvexa.SharedContracts.Workspaces.PermissionLevel's numeric value (0=View,
-- 1=Comment; the CHECK constraint below defends in depth against a value outside what a public link may
-- ever grant, even though the application enum has more levels for internal ACL grants). Default 0
-- (View) preserves today's behavior for every existing link. Safe on both an empty database and the
-- current already-migrated dev database (AGENTS.md rule 9).

ALTER TABLE sharing.share_links ADD COLUMN IF NOT EXISTS permission_level integer NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_share_links_permission_level'
    ) THEN
        ALTER TABLE sharing.share_links
            ADD CONSTRAINT ck_share_links_permission_level CHECK (permission_level IN (0, 1));
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS sharing.public_comments (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    share_link_id uuid NOT NULL,
    task_id uuid NOT NULL,
    guest_name character varying(120),
    body text NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    ip_address character varying(64),
    CONSTRAINT pk_public_comments PRIMARY KEY (id),
    CONSTRAINT fk_public_comments_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES tenancy.workspaces (id) ON DELETE CASCADE,
    CONSTRAINT fk_public_comments_share_links_share_link_id FOREIGN KEY (share_link_id) REFERENCES sharing.share_links (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_public_comments_workspace_share_link_created_at_utc
    ON sharing.public_comments (workspace_id, share_link_id, created_at_utc);

ALTER TABLE sharing.public_comments ENABLE ROW LEVEL SECURITY;
ALTER TABLE sharing.public_comments FORCE ROW LEVEL SECURITY;

-- Same workspace_isolation pattern as every workspace-owned table since 0029/0030 (see 0049's header).
-- Unlike sharing.share_links (which needs a maintenance-connection cross-workspace lookup by token hash
-- for the anonymous read path), inserting a guest comment happens AFTER ShareLinkService has already
-- resolved the link and established its workspace context (app.current_workspace), so the normal
-- application-role connection satisfies this policy without a maintenance connection.
DROP POLICY IF EXISTS workspace_isolation ON sharing.public_comments;
CREATE POLICY workspace_isolation ON sharing.public_comments USING (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
) WITH CHECK (
    nullif(current_setting('app.current_workspace', true), '') IS NOT NULL
    AND workspace_id = nullif(current_setting('app.current_workspace', true), '')::uuid
);
