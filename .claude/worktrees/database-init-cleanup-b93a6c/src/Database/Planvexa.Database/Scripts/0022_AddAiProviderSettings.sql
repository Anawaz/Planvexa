-- Per-tenant AI provider settings: a LiteLLM / OpenAI-compatible chat-completions endpoint. When absent
-- or disabled the API falls back to the offline deterministic provider. The API key is stored encrypted
-- (ASP.NET Core Data Protection) and never returned in plaintext.
--
-- RLS follows the hardened pattern from 0019/0021: a missing or empty app.current_tenant yields no rows
-- and blocks every write.

CREATE TABLE ai.provider_settings (
    id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    base_url character varying(500) NOT NULL,
    model character varying(200) NOT NULL,
    api_key_encrypted character varying(2000) NOT NULL DEFAULT '',
    is_enabled boolean NOT NULL DEFAULT false,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_provider_settings PRIMARY KEY (id),
    CONSTRAINT uq_provider_settings_tenant_id UNIQUE (tenant_id)
);

ALTER TABLE ai.provider_settings ENABLE ROW LEVEL SECURITY;

ALTER TABLE ai.provider_settings FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON ai.provider_settings
USING (
    nullif(current_setting('app.current_tenant', true), '') IS NOT NULL
    AND tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
)
WITH CHECK (
    nullif(current_setting('app.current_tenant', true), '') IS NOT NULL
    AND tenant_id = nullif(current_setting('app.current_tenant', true), '')::uuid
);
