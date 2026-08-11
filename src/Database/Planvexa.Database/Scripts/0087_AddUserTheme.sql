-- Planvexa DbUp script 0087_AddUserTheme.sql
-- Self-service theme preference: identity.users gets a nullable theme override so it can sync
-- across devices/sessions instead of living only in browser localStorage. NULL means "no account
-- preference yet" — the frontend falls back to localStorage / OS preference (ThemeContext in
-- apps/web). Value is one of 'light', 'dark', 'system' — validated in
-- UpdateDisplayNameRequestValidator / User.SetPreferences, not by a DB constraint.
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9). Existing rows default to NULL — nobody has set a preference yet.

ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS theme character varying(10);
