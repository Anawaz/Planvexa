-- Planvexa DbUp script 0069_AddIdempotencyKeysAndPushSubscriptionFields.sql
-- Two independent vertical slices for the offline PWA mutation-outbox replay work (the frontend half is
-- done separately in apps/web):
--
-- 1) Idempotency-Key dedup on the three create endpoints an offline outbox replays (task create, comment
--    create, timer start): a nullable idempotency_key column + a partial unique index on
--    (workspace_id, idempotency_key) WHERE idempotency_key IS NOT NULL, mirroring the same
--    check-before-create pattern ai.ai_requests.request_key and forms.form_submissions.idempotency_key
--    already use. NULL stays unconstrained (a create without the header behaves exactly as before).
--
-- 2) Mobile push: DeviceRegistration now stores the browser PushSubscription's raw endpoint/p256dh/auth
--    (nullable; TokenHash stays hashed for dedup lookups) -- see LoggingPushSender's doc comment for why
--    these are not secret credentials and what still turns this into a real Web Push sender.
--
-- ADD COLUMN IF NOT EXISTS / CREATE INDEX IF NOT EXISTS: safe on both an empty database and the current
-- already-migrated dev database (AGENTS.md rule 9).

ALTER TABLE work.tasks ADD COLUMN IF NOT EXISTS idempotency_key character varying(200);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tasks_workspace_id_idempotency_key ON work.tasks (workspace_id, idempotency_key) WHERE idempotency_key IS NOT NULL;

ALTER TABLE collab.comments ADD COLUMN IF NOT EXISTS idempotency_key character varying(200);
CREATE UNIQUE INDEX IF NOT EXISTS ux_comments_workspace_id_idempotency_key ON collab.comments (workspace_id, idempotency_key) WHERE idempotency_key IS NOT NULL;

ALTER TABLE time.time_entries ADD COLUMN IF NOT EXISTS idempotency_key character varying(200);
CREATE UNIQUE INDEX IF NOT EXISTS ux_time_entries_idempotency_key ON time.time_entries (workspace_id, idempotency_key) WHERE idempotency_key IS NOT NULL;

ALTER TABLE mobile.device_registrations ADD COLUMN IF NOT EXISTS push_endpoint character varying(2048);
ALTER TABLE mobile.device_registrations ADD COLUMN IF NOT EXISTS push_p256dh character varying(256);
ALTER TABLE mobile.device_registrations ADD COLUMN IF NOT EXISTS push_auth character varying(256);
