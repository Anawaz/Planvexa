-- Purge automated-test residue from a DEVELOPMENT database.
--
-- E2E runs write into the seeded demo workspace and register throwaway organizations, so a
-- long-lived dev database accumulates junk tasks and tenants that make the demo look broken.
-- Run against the dev database only:
--   psql -h localhost -U <superuser> -d planvexa -f scripts/purge-test-data.sql
--
-- What it removes:
--   * tenants whose slug matches the throwaway patterns below (all their data), and
--   * tasks in the surviving demo tenant whose title matches automated-test patterns.
-- Seeded rows use deterministic 018f0000-… identifiers and are never touched.

BEGIN;

CREATE TEMP TABLE purge_tenants ON COMMIT DROP AS
SELECT id FROM tenancy.tenants
WHERE slug ~ '^(e2e-|verify-org-|test-org-)';

CREATE TEMP TABLE purge_tasks ON COMMIT DROP AS
SELECT id FROM work.tasks
WHERE tenant_id IN (SELECT id FROM purge_tenants)
   OR (id::text NOT LIKE '018f0000%' AND title ~* '^(E2E |Probe |Smoke |VQ |Chunk )');

-- Task-scoped children first (only task_assignees/tags/watchers/attachments cascade).
DELETE FROM collab.mentions WHERE comment_id IN (
    SELECT id FROM collab.comments WHERE task_id IN (SELECT id FROM purge_tasks));
DELETE FROM collab.comment_reactions WHERE comment_id IN (
    SELECT id FROM collab.comments WHERE task_id IN (SELECT id FROM purge_tasks));
DELETE FROM collab.comments WHERE task_id IN (SELECT id FROM purge_tasks);
DELETE FROM work.task_checklist_items WHERE checklist_id IN (
    SELECT id FROM work.task_checklists WHERE task_id IN (SELECT id FROM purge_tasks));
DELETE FROM work.task_checklists WHERE task_id IN (SELECT id FROM purge_tasks);
DELETE FROM work.task_dependencies WHERE task_id IN (SELECT id FROM purge_tasks)
    OR depends_on_task_id IN (SELECT id FROM purge_tasks);
DELETE FROM work.custom_field_values WHERE task_id IN (SELECT id FROM purge_tasks);
DELETE FROM work.task_activity_events WHERE task_id IN (SELECT id FROM purge_tasks);
DELETE FROM sharing.share_links WHERE task_id IN (SELECT id FROM purge_tasks);
DELETE FROM planning.sprint_items WHERE task_id IN (SELECT id FROM purge_tasks);
DELETE FROM planning.task_estimates WHERE task_id IN (SELECT id FROM purge_tasks);
DELETE FROM "time".time_entries WHERE task_id IN (SELECT id FROM purge_tasks);
UPDATE docs.documents SET task_id = NULL WHERE task_id IN (SELECT id FROM purge_tasks);

-- Children of tasks being deleted whose parent link is another purged task.
UPDATE work.tasks SET parent_id = NULL
WHERE parent_id IN (SELECT id FROM purge_tasks) AND id NOT IN (SELECT id FROM purge_tasks);

DELETE FROM work.tasks WHERE id IN (SELECT id FROM purge_tasks);

-- Then everything owned by the throwaway tenants, child-first.
DELETE FROM work.custom_field_options WHERE definition_id IN (
    SELECT id FROM work.custom_field_definitions WHERE tenant_id IN (SELECT id FROM purge_tenants));
DELETE FROM work.custom_field_definitions WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM work.statuses WHERE scheme_id IN (
    SELECT id FROM work.status_schemes WHERE tenant_id IN (SELECT id FROM purge_tenants));
DELETE FROM work.status_schemes WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM work.tags WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM work.lists WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM work.folders WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM work.spaces WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM notifications.notification_deliveries WHERE notification_id IN (
    SELECT id FROM notifications.notifications WHERE tenant_id IN (SELECT id FROM purge_tenants));
DELETE FROM notifications.notifications WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM notifications.notification_preferences WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM tenancy.feature_entitlements WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM tenancy.invitations WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM tenancy.workspace_members WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM tenancy.workspaces WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM audit.audit_events WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM platform.outbox_messages WHERE tenant_id IN (SELECT id FROM purge_tenants);
DELETE FROM tenancy.tenants WHERE id IN (SELECT id FROM purge_tenants);

COMMIT;
