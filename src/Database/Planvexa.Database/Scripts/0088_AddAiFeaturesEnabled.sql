-- Planvexa DbUp script 0088_AddAiFeaturesEnabled.sql
-- Master "allow AI to be completely disabled" switch for a workspace, separate from
-- ai.provider_settings.is_enabled (which only chooses real-provider vs. offline-fallback routing).
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database and the current already-migrated dev
-- database (AGENTS.md rule 9). Defaults to true so upgrading an existing workspace never breaks its
-- current AI usage.

ALTER TABLE ai.provider_settings ADD COLUMN IF NOT EXISTS ai_features_enabled boolean NOT NULL DEFAULT true;
