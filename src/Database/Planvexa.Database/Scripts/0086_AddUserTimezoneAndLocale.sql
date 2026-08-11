-- Planvexa DbUp script 0086_AddUserTimezoneAndLocale.sql
-- Self-service display preferences: identity.users gets nullable timezone/locale overrides.
-- NULL means "use browser ambient" (timezone) / "use browser default" (locale) — the frontend only
-- overrides Intl.DateTimeFormat/NumberFormat calls when these are set. See User.SetPreferences.
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev database
-- (AGENTS.md rule 9). Existing rows default to NULL — nobody has set a preference yet.

ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS timezone character varying(100);
ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS locale character varying(35);
