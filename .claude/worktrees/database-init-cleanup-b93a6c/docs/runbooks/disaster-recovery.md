# Disaster recovery

What to do when a component is down, and in what order to bring things back — ordered by dependency,
since restoring in the wrong order just produces a second round of failures.

## Dependency order

```
PostgreSQL  ─┬─▶  API  ──▶  Web
Keycloak    ─┘
```

The API needs both Postgres (its own data) and Keycloak (to validate bearer tokens) to be reachable
before it reports `/health/ready`. The web app needs a healthy API (it proxies through the BFP at
`/api/proxy`) and Keycloak (OIDC sign-in) before it's usable. Restore in this order: **Postgres and
Keycloak first (parallel, they don't depend on each other), then API, then Web.**

## PostgreSQL is down or corrupted

1. If it's a managed instance (RDS, Cloud SQL, ...), check the provider's own status/failover first —
   Multi-AZ deployments (`multi_az = true` in `infrastructure/opentofu/modules/rds-postgres`) failover
   automatically; a single-AZ instance needs manual intervention.
2. If data is lost or corrupted beyond provider-level recovery, restore the latest backup — see
   `backup-restore.md`. If you're on RDS, prefer point-in-time recovery to a new instance and cut over
   the connection string, over `restore-db.ps1` into the same instance, when the incident allows it.
3. The API pods will be crash-looping or failing readiness while Postgres is unreachable
   (`/health/ready` depends on it) — no separate action needed on the API side once Postgres is back,
   `kubectl` restarts/readiness checks recover it automatically. If pods are stuck in a bad state
   regardless, `kubectl rollout restart deployment/<release>-api`.

## Keycloak is down

1. Existing signed-in sessions may keep working for a while (cached tokens), but no one can sign in,
   and the API rejects new/expired tokens once its authority is unreachable
   (`Keycloak__Authority` — see `Program.cs`'s comment on health probes surviving a misconfigured
   authority: the API's *health* endpoint stays up, but authenticated calls fail).
2. Recover Keycloak itself (restart the deployment, restore its own database if that's what failed —
   Keycloak has its own schema in whatever Postgres instance/database you pointed it at, separate from
   Planvexa's `planvexa` database, and is backed up/restored the same way if you're using
   `scripts/backup-db.ps1`/`restore-db.ps1` against that database too).
3. No Planvexa-side action needed once Keycloak is reachable again — the API re-validates against it
   per request, there's no cache to bust.

## API is down

1. Check `kubectl get pods` / `kubectl logs` first — most causes are either Postgres/Keycloak
   unreachable (fix those first, per above) or a bad rollout (see `upgrade.md`'s "if it fails partway"
   section for the DbUp-specific case).
2. If a bad deploy is the cause and you're mid-upgrade, `helm rollback planvexa <previous-revision>`
   rolls the *image* back — re-read `upgrade.md`'s forward-only caveat first if DbUp scripts already
   ran; rolling the image back past a schema change it doesn't expect can be worse than staying on the
   broken version while you fix forward.
3. Once the pods report `Ready`, the web app recovers automatically (it calls the API per-request,
   there's nothing cached to restart).

## Web is down

1. Same first move: `kubectl get pods` / `kubectl logs`.
2. The web app has no state of its own beyond `PLANVEXA_WEB_SESSION_SECRET` (session cookie signing) —
   if that Secret changed, every existing session cookie is invalidated (users get redirected to sign
   in again, not an error page). That's an expected side effect of rotating it, not a bug.
3. Restart/rollout-restart is almost always sufficient once the underlying cause (bad image, resource
   limits, config error) is fixed — there's no data to restore for the web tier itself.

## After the incident

Confirm `/health/live` and `/health/ready` are green on the API, `/login` loads on the web app, and
run a restore drill (`backup-restore.md`'s "Restore drills" section) if the incident involved data
loss, to confirm the backup you'd rely on next time actually restores cleanly.
