-- Planvexa DbUp script 0089_AddAiCreditLimit.sql
-- Optional monthly cap (calendar month, UTC) on estimated tokens spent through a workspace's real AI
-- provider. Null (the default) means unlimited, so upgrading an existing workspace never breaks its
-- current AI usage -- only a real, cost-incurring provider call is ever blocked, never the offline
-- extractive fallback.
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9).

ALTER TABLE ai.provider_settings ADD COLUMN IF NOT EXISTS credit_limit integer NULL;
