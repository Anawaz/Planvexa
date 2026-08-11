# Runbooks

Operational documentation for running Planvexa outside local development. Local dev is covered by the
main [`README.md`](../../README.md#getting-started-local-development); these runbooks pick up where
that leaves off.

| Runbook | Covers |
| --- | --- |
| [`install.md`](install.md) | Fresh production install: DbUp on first boot, prod vs. dev seeding |
| [`upgrade.md`](upgrade.md) | Version upgrades; DbUp is forward-only, what that means for you |
| [`backup-restore.md`](backup-restore.md) | Using `scripts/backup-db.ps1` / `scripts/restore-db.ps1` in production |
| [`disaster-recovery.md`](disaster-recovery.md) | API/web/Postgres/Keycloak down — what to check, in what order |

These assume you're deploying with `infrastructure/helm/planvexa` (see
[`infrastructure/helm/README.md`](../../infrastructure/helm/README.md)); the same DbUp/backup/restore
behavior applies however you run the `planvexa-api` image.
