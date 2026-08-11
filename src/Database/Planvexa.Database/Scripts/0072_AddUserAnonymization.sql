-- Planvexa DbUp script 0072_AddUserAnonymization.sql
-- GDPR-style user-data export/deletion: identity.users gets an anonymization flag.
--
-- User rows are never hard-deleted on account deletion — Id is referenced as an author/assignee/actor FK
-- across other modules' tables (work.tasks.created_by_user_id, collab.comments.author_user_id,
-- time.time_entries.user_id, audit.audit_events.actor_user_id, ...), and hard-deleting the row would
-- either break those references or require rewriting every referencing row in every module (a module
-- must not reach into another module's tables per AGENTS.md, so that would mean a much larger blast
-- radius for no visible benefit). Instead, User.Anonymize() scrubs Subject/Email/DisplayName in place and
-- flips is_active off; every place that resolves the (unchanged) UserId to a display name then shows the
-- scrubbed "Deleted User" values automatically, with no other module's tables touched at all. See
-- Planvexa.Modules.Identity.Domain.User's IsAnonymized doc comment for the full reasoning.
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9). Existing rows default to is_anonymized = false, which is correct — no user has been
-- through the new deletion flow yet.

ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS is_anonymized boolean NOT NULL DEFAULT false;
ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS anonymized_at_utc timestamp with time zone;
