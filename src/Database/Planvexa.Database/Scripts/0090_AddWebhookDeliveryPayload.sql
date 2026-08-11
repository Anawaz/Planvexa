-- Planvexa DbUp script 0090_AddWebhookDeliveryPayload.sql
-- Persists the exact signed JSON body sent for each webhook delivery attempt, so a failed delivery can be
-- manually retried (replayed) without the original, ephemeral WorkspaceEvent still being available.
-- Nullable: existing rows predate this field and simply cannot be retried (WebhookService.RetryDeliveryAsync
-- rejects a null payload). ADD COLUMN IF NOT EXISTS is safe on both an empty database and the current
-- already-migrated dev database (AGENTS.md rule 9).

ALTER TABLE integrations.webhook_deliveries ADD COLUMN IF NOT EXISTS payload_json jsonb NULL;
