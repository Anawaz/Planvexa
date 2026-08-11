# Version upgrades

## DbUp is forward-only — what that actually means

`src/Database/Planvexa.Database/Scripts` is an ordered, numbered sequence of SQL scripts
(`0001_...sql` … `0072_...sql` and counting). `PlanvexaDatabase.Upgrade` (via DbUp) records every
applied script in a `platform.schema_versions` journal table and, on startup, runs whichever scripts
in that sequence the target database hasn't seen yet — always in order, always forward. There is no
"migrate down" command and no down-scripts:

- **You cannot roll a database back to an older schema version by re-running Planvexa.** If a new
  release's schema change turns out to be wrong, the fix is a *new* forward script that corrects it —
  same as any forward-only migration tool (Rails, Flyway, EF Core migrations used the same way).
- **A given API image version expects its own schema version or later.** The DbUp scripts are
  embedded in the `Planvexa.Database` assembly, which ships inside the `planvexa-api` image
  (`infrastructure/docker/api.Dockerfile`) — so "which scripts exist" is fixed at the image tag you
  deployed, not something you configure separately.
- **Rolling back to an older image after its DbUp scripts already ran is unsafe** if the newer
  scripts changed table shapes the older code doesn't expect (dropped/renamed columns, changed
  constraints). Whether a specific upgrade is safe to roll back the *image* on (leaving the *schema*
  forward) depends on that release's specific scripts — check the new scripts added between your old
  and new versions before assuming a same-schema rollback is safe.

## Upgrade procedure

1. **Read the new scripts.** Between the version you're running and the version you're upgrading to,
   look at what's new under `src/Database/Planvexa.Database/Scripts` (diff the two tags/releases).
   Anything destructive (dropped columns, `NOT NULL` added to existing data) is called out in that
   release's notes if the project publishes them.
2. **Back up first.** Always — see `backup-restore.md`. This is your only rollback path once a
   forward-only migration has run.
3. **Deploy the new image.**
   ```bash
   helm upgrade planvexa infrastructure/helm/planvexa \
     --reuse-values \
     --set api.image.tag=<new tag> \
     --set web.image.tag=<new tag>
   ```
   With `Database__RunDbUpOnStartup=true` (the default), the new API pods run the newly-added DbUp
   scripts automatically as they start — same advisory-lock serialization across replicas described
   in `install.md`. There is no separate "run migrations" step to remember.
4. **Watch the rollout.** `kubectl rollout status deployment/<release>-api`. A pod stuck `0/1 Ready`
   during an upgrade is very likely still applying DbUp scripts (or blocked on the advisory lock behind
   another replica that is) — check `kubectl logs` before assuming a crash.
5. **If it fails partway through a script:** DbUp wraps each script in its own transaction
   (`WithTransactionPerScript`), so a script that errors rolls back cleanly and the journal is not
   updated for it — the database is left at the last successfully-applied script, not half-migrated.
   Fix forward (new script) or restore the pre-upgrade backup; do not hand-edit the journal table.

## Running migrations separately from the API rollout (optional)

If you'd rather not have every API replica racing for the advisory lock on every deploy, run a single
one-off job first (`kubectl run` or a Helm `pre-upgrade` hook running the same `planvexa-api` image
with `Database__RunDbUpOnStartup=true` and 0 replicas afterward would work, or simply deploy the API
with `replicas: 1` for the upgrade and scale back up once healthy) — DbUp itself doesn't require this,
it's purely about controlling *when* the (brief) migration window happens relative to your rollout.
