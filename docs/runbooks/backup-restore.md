# Backup and restore

Planvexa doesn't run its own backup daemon or duplicate `pg_dump`/`pg_restore` logic — use the
existing scripts, same as local dev:

- [`scripts/backup-db.ps1`](../../scripts/backup-db.ps1) — `pg_dump -Fc` (custom format) to
  `.data/backups/planvexa-<timestamp>.dump`, or a path you pass via `-OutputPath`.
- [`scripts/restore-db.ps1`](../../scripts/restore-db.ps1) — `pg_restore --clean --if-exists
  --no-owner` from the latest `.dump` in `.data/backups/`, or a path you pass via `-InputPath`.

Both scripts use the local `pg_dump`/`pg_restore` client if one is on `PATH`, and fall back to a
throwaway `postgres:18-alpine` Docker container otherwise — so they work the same way against a
production database as they do locally, as long as you point `-ConnectionString` (or the
`PLANVEXA_CONNECTION_STRING` env var) at it instead of `localhost`.

## Production usage

```powershell
# Backup, against your production database, from wherever pwsh + Docker (or pg_dump) run:
pwsh scripts/backup-db.ps1 `
  -ConnectionString "Host=<prod-host>;Port=5432;Database=planvexa;Username=planvexa;Password=<...>" `
  -OutputPath "backups/planvexa-prod-$(Get-Date -Format yyyyMMdd-HHmmss).dump"

# Restore (into an EXISTING empty or previously-Planvexa database -- --clean drops and recreates
# objects it owns before restoring, it does not create the database itself):
pwsh scripts/restore-db.ps1 `
  -ConnectionString "Host=<prod-host>;Port=5432;Database=planvexa;Username=planvexa;Password=<...>" `
  -InputPath "backups/planvexa-prod-20260101-020000.dump"
```

If you provisioned Postgres with `infrastructure/opentofu/modules/rds-postgres`, RDS's own automated
backups (`backup_retention_period`, default 7 days) run independently of these scripts and cover
point-in-time recovery — `backup-db.ps1`/`restore-db.ps1` are for portable, on-demand logical dumps
(moving data between environments, a backup you control the retention of yourself, restoring a single
table's worth of data via `pg_restore -t`, etc.), not a replacement for RDS's own snapshots.

## Scheduling

Neither script schedules itself. Run `backup-db.ps1` on whatever scheduler your platform already
gives you (a Kubernetes `CronJob` running an image with `pwsh` + `pg_dump`, a cloud provider's
scheduled task, cron on a jump box) and ship the `.dump` output somewhere durable (object storage —
the same bucket `infrastructure/opentofu/modules/s3-storage` provisions for file attachments works,
in a separate prefix/bucket if you want independent lifecycle rules).

## Restore drills

A backup you haven't restored is unverified. Periodically restore the latest dump into a scratch
database (not production) and spot-check row counts / a few known records — `restore-db.ps1` accepts
any `-ConnectionString`, so pointing it at a throwaway database is the same command with a different
target.
