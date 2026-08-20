-- Planvexa DbUp script 0096_AddInstanceLogs.sql
-- The host console's instance log store: warnings and errors captured from ILogger and kept for a
-- short, configurable window (InstanceLogs:RetentionDays, default 14) so the operator can see what
-- broke without shell access to the server. The OpenTelemetry -> Loki/Grafana pipeline remains the
-- system of record for full-fidelity logs; this is the operator-visible slice of it.
--
-- No RLS, matching platform.outbox_messages and platform.instance_settings: these records describe the
-- process, not a Workspace. workspace_id is captured when a record happens to originate inside a
-- Workspace-scoped request, purely as a filter for the console -- it is NOT an isolation key, and the
-- endpoint reading this table is host-admin-only.
--
-- PRIVACY: message/exception are whatever the application logged and may contain user data. Retention
-- is what bounds that, which is why the sweep in InstanceLogBackgroundService is not optional and the
-- default minimum level is Warning rather than everything.
--
-- Indexes: the console lists newest-first (created_at_utc DESC) and filters by level far more often
-- than by anything else, so those two cover every query the endpoint issues. The retention sweep is a
-- range delete on created_at_utc and rides the first index.

CREATE TABLE IF NOT EXISTS platform.instance_logs (
    id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    level character varying(16) NOT NULL,
    category character varying(256) NOT NULL,
    message text NOT NULL,
    exception text,
    correlation_id character varying(128),
    user_id uuid,
    workspace_id uuid,
    CONSTRAINT pk_instance_logs PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_instance_logs_created_at ON platform.instance_logs (created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_instance_logs_level_created_at ON platform.instance_logs (level, created_at_utc DESC);
