-- Planvexa DbUp script 0091_DedupeUsersAndEnforceUniqueIdentity.sql
--
-- identity.users could end up with two rows for the same email. Root cause: UserDirectory.
-- GetOrProvisionAsync does a check-then-create (find by subject, find by email, else insert) with no
-- database-level uniqueness backing it, so several parallel authenticated requests for the same
-- brand-new subject/email (e.g. the frontend firing /users/me, /workspaces/me, /features back-to-back
-- right after a fresh sign-in, or a SignalR hub connection racing the HTTP request — WorkspaceHub.
-- ResolveContextAsync and UserContextMiddleware each call GetOrProvisionAsync independently) can all
-- pass the "not found" checks before any of them commits. UserConfiguration.cs already declares
-- HasIndex(u => u.Subject/Email).IsUnique() on the EF model, but this schema is DbUp-authoritative
-- (EF migrations are not used to create it — see README), and no DbUp script ever added the matching
-- indexes, so nothing ever enforced it.
--
-- Part 1 merges any existing duplicates (keep the oldest row per lowercased email, repoint every
-- known reference, drop the rest). Part 2 adds the missing unique indexes so it can't happen again;
-- UserDirectory.GetOrProvisionAsync now also catches the resulting DbUpdateException on the losing
-- side of the race and re-reads the winner instead of erroring.

CREATE TEMP TABLE user_dedup_map AS
SELECT u.id AS lose_id, keeper.id AS keep_id
FROM identity.users u
JOIN LATERAL (
    SELECT k.id
    FROM identity.users k
    WHERE lower(k.email) = lower(u.email)
    ORDER BY k.created_at_utc ASC, k.id ASC
    LIMIT 1
) keeper ON true
WHERE keeper.id <> u.id;

-- Columns with no uniqueness constraint of their own: a plain repoint is always safe.
UPDATE tenancy.workspaces t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE tenancy.teams t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE tenancy.invitations t SET invited_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.invited_by_user_id = m.lose_id;
UPDATE tenancy.invitations t SET accepted_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.accepted_by_user_id = m.lose_id;
UPDATE tenancy.resource_permissions t SET granted_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.granted_by_user_id = m.lose_id;

UPDATE work.spaces t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE work.spaces t SET updated_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.updated_by_user_id = m.lose_id;
UPDATE work.spaces t SET deleted_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.deleted_by_user_id = m.lose_id;
UPDATE work.folders t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE work.folders t SET updated_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.updated_by_user_id = m.lose_id;
UPDATE work.folders t SET deleted_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.deleted_by_user_id = m.lose_id;
UPDATE work.lists t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE work.lists t SET updated_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.updated_by_user_id = m.lose_id;
UPDATE work.lists t SET deleted_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.deleted_by_user_id = m.lose_id;
UPDATE work.tasks t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE work.tasks t SET updated_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.updated_by_user_id = m.lose_id;
UPDATE work.tasks t SET deleted_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.deleted_by_user_id = m.lose_id;
UPDATE work.tasks t SET completed_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.completed_by_user_id = m.lose_id;
UPDATE work.task_activity_events t SET actor_user_id = m.keep_id FROM user_dedup_map m WHERE t.actor_user_id = m.lose_id;
UPDATE work.recurring_task_definitions t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE work.saved_views t SET owner_user_id = m.keep_id FROM user_dedup_map m WHERE t.owner_user_id = m.lose_id;
UPDATE work.task_attachments t SET uploaded_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.uploaded_by_user_id = m.lose_id;
UPDATE work.task_reminders t SET user_id = m.keep_id FROM user_dedup_map m WHERE t.user_id = m.lose_id;
UPDATE work.custom_field_values t SET user_value = m.keep_id FROM user_dedup_map m WHERE t.user_value = m.lose_id;
UPDATE work.work_templates t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE work.import_jobs t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;

UPDATE collab.comments t SET author_user_id = m.keep_id FROM user_dedup_map m WHERE t.author_user_id = m.lose_id;
UPDATE collab.comments t SET deleted_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.deleted_by_user_id = m.lose_id;
UPDATE collab.comment_attachments t SET uploaded_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.uploaded_by_user_id = m.lose_id;

UPDATE sharing.share_links t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;

UPDATE time.time_entry_audits t SET actor_user_id = m.keep_id FROM user_dedup_map m WHERE t.actor_user_id = m.lose_id;
UPDATE time.timesheet_approvals t SET approver_user_id = m.keep_id FROM user_dedup_map m WHERE t.approver_user_id = m.lose_id;
UPDATE time.timesheet_periods t SET approved_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.approved_by_user_id = m.lose_id;

UPDATE planning.leave_entries t SET user_id = m.keep_id FROM user_dedup_map m WHERE t.user_id = m.lose_id;
UPDATE planning.sprints t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;

UPDATE reporting.dashboards t SET owner_user_id = m.keep_id FROM user_dedup_map m WHERE t.owner_user_id = m.lose_id;
UPDATE reporting.risks t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE reporting.scheduled_reports t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE reporting.portfolios t SET owner_user_id = m.keep_id FROM user_dedup_map m WHERE t.owner_user_id = m.lose_id;

UPDATE docs.documents t SET owner_user_id = m.keep_id FROM user_dedup_map m WHERE t.owner_user_id = m.lose_id;
UPDATE docs.document_versions t SET author_user_id = m.keep_id FROM user_dedup_map m WHERE t.author_user_id = m.lose_id;
UPDATE docs.document_templates t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE docs.document_share_links t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE docs.document_comments t SET author_user_id = m.keep_id FROM user_dedup_map m WHERE t.author_user_id = m.lose_id;

UPDATE forms.forms t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE forms.forms t SET target_user_id = m.keep_id FROM user_dedup_map m WHERE t.target_user_id = m.lose_id;

UPDATE automation.automation_rules t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE automation.automation_runs t SET actor_user_id = m.keep_id FROM user_dedup_map m WHERE t.actor_user_id = m.lose_id AND t.actor_user_id <> '00000000-0000-0000-0000-000000000000';
UPDATE automation.automation_rule_versions t SET changed_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.changed_by_user_id = m.lose_id;

UPDATE integrations.personal_access_tokens t SET user_id = m.keep_id FROM user_dedup_map m WHERE t.user_id = m.lose_id;
UPDATE integrations.webhook_subscriptions t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE integrations.oauth_applications t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE integrations.oauth_authorization_codes t SET user_id = m.keep_id FROM user_dedup_map m WHERE t.user_id = m.lose_id;
UPDATE integrations.oauth_tokens t SET user_id = m.keep_id FROM user_dedup_map m WHERE t.user_id = m.lose_id;

UPDATE governance.export_jobs t SET requested_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.requested_by_user_id = m.lose_id;

UPDATE ai.ai_requests t SET user_id = m.keep_id FROM user_dedup_map m WHERE t.user_id = m.lose_id;

UPDATE chat.channels t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE chat.messages t SET author_user_id = m.keep_id FROM user_dedup_map m WHERE t.author_user_id = m.lose_id;
UPDATE chat.attachments t SET uploaded_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.uploaded_by_user_id = m.lose_id;

UPDATE goals.goal_folders t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;
UPDATE goals.goals t SET owner_user_id = m.keep_id FROM user_dedup_map m WHERE t.owner_user_id = m.lose_id;
UPDATE goals.goal_comments t SET author_user_id = m.keep_id FROM user_dedup_map m WHERE t.author_user_id = m.lose_id;

UPDATE whiteboards.whiteboards t SET owner_user_id = m.keep_id FROM user_dedup_map m WHERE t.owner_user_id = m.lose_id;
UPDATE whiteboards.whiteboard_templates t SET created_by_user_id = m.keep_id FROM user_dedup_map m WHERE t.created_by_user_id = m.lose_id;

UPDATE clips.clips t SET owner_user_id = m.keep_id FROM user_dedup_map m WHERE t.owner_user_id = m.lose_id;
UPDATE clips.clip_comments t SET author_user_id = m.keep_id FROM user_dedup_map m WHERE t.author_user_id = m.lose_id;

UPDATE audit.audit_events t SET actor_user_id = m.keep_id FROM user_dedup_map m WHERE t.actor_user_id = m.lose_id;

-- Columns where the user id participates in a composite (or, for my_work_preferences, solitary) unique
-- constraint: repoint only rows that would not collide with a row the keeper already has for that key,
-- then drop whatever is left pointing at the loser — a genuine duplicate of the keeper's own row for
-- that key, which is exactly what "these are the same identity" means for a unique-per-user fact.
-- IS NOT DISTINCT FROM handles nullable key columns correctly (NULL = NULL).

-- tenancy.workspace_members: ux_workspace_members_workspace_id_user_id (workspace_id, user_id)
UPDATE tenancy.workspace_members t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM tenancy.workspace_members k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id);
DELETE FROM tenancy.workspace_members t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- tenancy.resource_permissions.principal_id: ux_resource_permissions_resource_principal
-- (resource_type, resource_id, principal_type, principal_id) — only relevant when principal_type='user'.
UPDATE tenancy.resource_permissions t SET principal_id = m.keep_id
FROM user_dedup_map m
WHERE t.principal_id = m.lose_id AND t.principal_type = 'user'
  AND NOT EXISTS (
    SELECT 1 FROM tenancy.resource_permissions k
    WHERE k.principal_id = m.keep_id AND k.principal_type = 'user'
      AND k.resource_type = t.resource_type AND k.resource_id = t.resource_id);
DELETE FROM tenancy.resource_permissions t USING user_dedup_map m WHERE t.principal_id = m.lose_id AND t.principal_type = 'user';

-- tenancy.team_members: (team_id, user_id) has no DB-enforced uniqueness (ADR-0015's tenant->workspace
-- cutover dropped the old tenant-scoped one via DROP COLUMN ... CASCADE and never added a
-- workspace-scoped replacement — a separate pre-existing gap, out of scope here), but avoid creating an
-- application-visible duplicate membership row while merging anyway.
UPDATE tenancy.team_members t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM tenancy.team_members k WHERE k.user_id = m.keep_id AND k.team_id = t.team_id);
DELETE FROM tenancy.team_members t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- work.task_assignees: ux_task_assignees_workspace_id_task_id_user_id
UPDATE work.task_assignees t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM work.task_assignees k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.task_id = t.task_id);
DELETE FROM work.task_assignees t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- work.task_watchers: ux_task_watchers_workspace_id_task_id_user_id
UPDATE work.task_watchers t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM work.task_watchers k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.task_id = t.task_id);
DELETE FROM work.task_watchers t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- work.work_favorites: ux_work_favorites_workspace_user_resource (workspace_id, user_id, resource_type, resource_id)
UPDATE work.work_favorites t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM work.work_favorites k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.resource_type = t.resource_type AND k.resource_id = t.resource_id);
DELETE FROM work.work_favorites t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- work.recent_items: ux_recent_items_workspace_user_resource (workspace_id, user_id, resource_type, resource_id)
UPDATE work.recent_items t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM work.recent_items k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.resource_type = t.resource_type AND k.resource_id = t.resource_id);
DELETE FROM work.recent_items t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- work.my_work_preferences: ux_my_work_preferences_user_id — UNIQUE(user_id) alone, global per-user.
DELETE FROM work.my_work_preferences t
USING user_dedup_map m
WHERE t.user_id = m.lose_id AND EXISTS (SELECT 1 FROM work.my_work_preferences k WHERE k.user_id = m.keep_id);
UPDATE work.my_work_preferences t SET user_id = m.keep_id FROM user_dedup_map m WHERE t.user_id = m.lose_id;

-- collab.comment_reactions: ux_comment_reactions_workspace_id_comment_id_user_id_emoji
UPDATE collab.comment_reactions t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM collab.comment_reactions k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.comment_id = t.comment_id AND k.emoji = t.emoji);
DELETE FROM collab.comment_reactions t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- collab.mentions: ux_mentions_workspace_id_comment_id_mentioned_user_id
UPDATE collab.mentions t SET mentioned_user_id = m.keep_id
FROM user_dedup_map m
WHERE t.mentioned_user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM collab.mentions k WHERE k.mentioned_user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.comment_id = t.comment_id);
DELETE FROM collab.mentions t USING user_dedup_map m WHERE t.mentioned_user_id = m.lose_id;

-- notifications.notification_preferences: ux_notification_preferences_workspace_id_user_id_event_type
UPDATE notifications.notification_preferences t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM notifications.notification_preferences k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.event_type = t.event_type);
DELETE FROM notifications.notification_preferences t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- notifications.digest_preferences: ux_digest_preferences_workspace_user (workspace_id, user_id)
UPDATE notifications.digest_preferences t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM notifications.digest_preferences k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id);
DELETE FROM notifications.digest_preferences t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- notifications.notifications: ux_notifications_workspace_id_recipient_user_id_deduplication_k
UPDATE notifications.notifications t SET recipient_user_id = m.keep_id
FROM user_dedup_map m
WHERE t.recipient_user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM notifications.notifications k WHERE k.recipient_user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.deduplication_key IS NOT DISTINCT FROM t.deduplication_key);
DELETE FROM notifications.notifications t USING user_dedup_map m WHERE t.recipient_user_id = m.lose_id;

-- time.time_entries: ux_time_entries_workspace_id_user_id (workspace_id, user_id) WHERE ended_at_utc IS
-- NULL — only a running (unended) timer can collide; completed entries have no constraint at all.
UPDATE time.time_entries t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND (t.ended_at_utc IS NOT NULL
       OR NOT EXISTS (SELECT 1 FROM time.time_entries k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.ended_at_utc IS NULL));
DELETE FROM time.time_entries t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- time.timesheet_periods: ux_timesheet_periods_workspace_id_user_id_period_start_utc
UPDATE time.timesheet_periods t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM time.timesheet_periods k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.period_start_utc = t.period_start_utc);
DELETE FROM time.timesheet_periods t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- time.member_rates: ux_member_rates_workspace_id_user_id_project_id (project_id is nullable: "default rate").
UPDATE time.member_rates t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM time.member_rates k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.project_id IS NOT DISTINCT FROM t.project_id);
DELETE FROM time.member_rates t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- chat.channel_members: ux_channel_members_workspace_id_channel_id_user_id
UPDATE chat.channel_members t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM chat.channel_members k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.channel_id = t.channel_id);
DELETE FROM chat.channel_members t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- chat.channel_read_states: ux_channel_read_states_channel_id_user_id
UPDATE chat.channel_read_states t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM chat.channel_read_states k WHERE k.user_id = m.keep_id AND k.channel_id = t.channel_id);
DELETE FROM chat.channel_read_states t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- chat.message_reactions: ux_message_reactions_message_id_user_id_emoji
UPDATE chat.message_reactions t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM chat.message_reactions k WHERE k.user_id = m.keep_id AND k.message_id = t.message_id AND k.emoji = t.emoji);
DELETE FROM chat.message_reactions t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- chat.mentions: ux_mentions_message_id_mentioned_user_id (a different table from collab.mentions above)
UPDATE chat.mentions t SET mentioned_user_id = m.keep_id
FROM user_dedup_map m
WHERE t.mentioned_user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM chat.mentions k WHERE k.mentioned_user_id = m.keep_id AND k.message_id = t.message_id);
DELETE FROM chat.mentions t USING user_dedup_map m WHERE t.mentioned_user_id = m.lose_id;

-- mobile.device_registrations: ux_device_registrations_workspace_id_user_id_token_hash
UPDATE mobile.device_registrations t SET user_id = m.keep_id
FROM user_dedup_map m
WHERE t.user_id = m.lose_id
  AND NOT EXISTS (SELECT 1 FROM mobile.device_registrations k WHERE k.user_id = m.keep_id AND k.workspace_id = t.workspace_id AND k.token_hash = t.token_hash);
DELETE FROM mobile.device_registrations t USING user_dedup_map m WHERE t.user_id = m.lose_id;

-- Every reference is repointed and every genuine duplicate resolved — drop the losing identity rows.
DELETE FROM identity.users u USING user_dedup_map m WHERE u.id = m.lose_id;

DROP TABLE user_dedup_map;

-- The actual fix: back the EF model's already-declared IsUnique() indexes (UserConfiguration.cs) with
-- real ones, so this dedup is never needed again. IF NOT EXISTS: safe on both an empty database and
-- the current already-migrated dev database (AGENTS.md rule 9).
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_subject ON identity.users (subject);
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON identity.users (email);
