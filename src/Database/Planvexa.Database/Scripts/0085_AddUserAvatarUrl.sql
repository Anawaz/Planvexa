-- Planvexa DbUp script 0085_AddUserAvatarUrl.sql
-- Self-service avatar upload (POST /api/v1/users/me/avatar): identity.users gets a nullable pointer to
-- the uploaded profile picture. Stores the relative API path that serves it (e.g. /users/{id}/avatar),
-- never a raw storage path or presigned URL — see User.SetAvatarUrl.
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9). Existing rows default to NULL — nobody has uploaded one yet, so the frontend keeps
-- falling back to initials for them.

ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS avatar_url character varying(300);
