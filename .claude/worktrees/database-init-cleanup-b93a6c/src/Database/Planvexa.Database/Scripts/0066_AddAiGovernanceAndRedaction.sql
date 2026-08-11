-- Planvexa DbUp script 0066_AddAiGovernanceAndRedaction.sql
-- AI capability expansion: a per-workspace admin-configurable model allow-list on
-- ai.provider_settings, plus the redaction pass's toggles and custom patterns; and the redaction audit
-- trail (count + pattern types, never the matched value) on ai.ai_requests.
--
-- ADD COLUMN IF NOT EXISTS: safe on both an empty database (this file runs after 0022/0015 create the
-- base tables) and the current already-migrated dev database (AGENTS.md rule 9). Defaults match the
-- domain's own defaults (empty allow-list = unrestricted; every built-in redaction toggle on).

ALTER TABLE ai.provider_settings ADD COLUMN IF NOT EXISTS allowed_models_json jsonb NOT NULL DEFAULT '[]';
ALTER TABLE ai.provider_settings ADD COLUMN IF NOT EXISTS redact_emails boolean NOT NULL DEFAULT true;
ALTER TABLE ai.provider_settings ADD COLUMN IF NOT EXISTS redact_api_keys boolean NOT NULL DEFAULT true;
ALTER TABLE ai.provider_settings ADD COLUMN IF NOT EXISTS redact_credit_cards boolean NOT NULL DEFAULT true;
ALTER TABLE ai.provider_settings ADD COLUMN IF NOT EXISTS custom_redaction_patterns_json jsonb NOT NULL DEFAULT '[]';

ALTER TABLE ai.ai_requests ADD COLUMN IF NOT EXISTS redacted_count integer NOT NULL DEFAULT 0;
ALTER TABLE ai.ai_requests ADD COLUMN IF NOT EXISTS redacted_types character varying(200) NOT NULL DEFAULT '';
