-- Planvexa DbUp script 0036_AddShareLinkPassword.sql
-- ADR-0003: optional password protection for public task share links. password_hash stores a
-- PBKDF2 hash (format "{iterations}.{saltBase64}.{hashBase64}", see PublicShareLink.SetPassword) — never
-- the raw password. Null means "no password required" (today's behavior, unchanged). Safe on both an
-- empty database and the current already-migrated dev database (AGENTS.md rule 9).

ALTER TABLE sharing.share_links ADD COLUMN IF NOT EXISTS password_hash text;
