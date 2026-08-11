-- Planvexa local PostgreSQL initialization.
-- The official postgres image runs this script against POSTGRES_DB (planvexa) on first volume creation.

SELECT 'CREATE DATABASE keycloak OWNER planvexa'
WHERE NOT EXISTS (
  SELECT 1
  FROM pg_database
  WHERE datname = 'keycloak'
)
\gexec

\connect planvexa

-- Optional helper extensions. The application generates UUIDv7 values in code, so pgcrypto/uuid-ossp
-- are available for local SQL utilities but are not required for primary key generation.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'planvexa_app') THEN
    CREATE ROLE planvexa_app NOLOGIN;
  END IF;
END;
$$;

COMMENT ON ROLE planvexa_app IS 'NOLOGIN placeholder role for future Planvexa row-level security policies and GRANT targets.';