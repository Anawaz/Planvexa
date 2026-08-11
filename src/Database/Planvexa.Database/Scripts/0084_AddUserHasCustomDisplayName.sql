-- Planvexa DbUp script 0084_AddUserHasCustomDisplayName.sql
-- Self-service profile rename (PATCH /api/v1/users/me): identity.users gets a flag marking that the
-- user has set their own display name.
--
-- Every authenticated request re-provisions the user via UserDirectory.GetOrProvisionAsync, which calls
-- User.SyncProfile(email, name, ...) with the IdP's current claims. Without this flag, that sync would
-- silently overwrite a self-service rename back to the IdP-supplied name on the very next request. Once
-- set, SyncProfile leaves DisplayName alone (Email is still kept in sync from the IdP either way).
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9). Existing rows default to false — nobody has renamed themselves yet, so IdP sync
-- should keep behaving exactly as before for them.

ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS has_custom_display_name boolean NOT NULL DEFAULT false;
