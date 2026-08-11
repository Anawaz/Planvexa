# Fresh production install

## Prerequisites

Same external dependencies as local dev, minus the dev-only conveniences:

| Dependency | Notes |
| --- | --- |
| PostgreSQL 18 | Host-provided — Planvexa never starts, stops or configures the server itself. See `infrastructure/opentofu/modules/rds-postgres` for an example provisioning module, or bring your own. |
| Keycloak | A realm with the API/web clients configured (mirror what `scripts/keycloak-bootstrap.ps1` sets up for dev — client IDs `planvexa-api` / `planvexa-web`). Not bundled by the Helm chart. |
| S3-compatible object storage | Only if `FileStorage__Provider=S3`; local disk works for a single-replica install. See `infrastructure/opentofu/modules/s3-storage`. |
| Kubernetes cluster + `helm` | See `infrastructure/helm/README.md`. |

## 1. Create the login role

Planvexa never creates the PostgreSQL *server* or its *login roles* — those are yours. It does create
its own database and schema, so the only mandatory step is the role:

```sql
CREATE ROLE planvexa LOGIN CREATEDB PASSWORD '<strong password>';
```

With `CREATEDB`, `PlanvexaDatabase.Upgrade` creates the database itself on first boot (DbUp's
`EnsureDatabase`), so a wiped or never-created database recovers without manual intervention — the
same path local dev takes. If your policy forbids `CREATEDB` on the application login, drop it and
create the database by hand instead:

```sql
CREATE ROLE planvexa LOGIN PASSWORD '<strong password>';
CREATE DATABASE planvexa OWNER planvexa;
```

Optional: the `planvexa_maint` role (`BYPASSRLS`) for cross-workspace background sweeps — see
`ConnectionStrings:PlanvexaMaintenance` in `src/Infrastructure/Planvexa.Infrastructure/Persistence/MaintenanceConnection.cs`.
Without it, those sweeps fall back to the application connection, which is safe but not the
long-term-recommended shape for a production install:

```sql
CREATE ROLE planvexa_maint LOGIN BYPASSRLS PASSWORD '<strong password>';
```

## 2. Create the Kubernetes Secrets and install

Follow `infrastructure/helm/README.md`'s Install section: create `planvexa-api-secrets` (with
`ConnectionStrings__Planvexa` pointed at the database from step 1) and `planvexa-web-secrets`, then
`helm install`.

## 3. First boot: DbUp runs automatically

With `Database__RunDbUpOnStartup=true` (the chart's default — `values.yaml`'s
`api.config.runDbUpOnStartup`), the API applies every DbUp script under
`src/Database/Planvexa.Database/Scripts` in order on startup, against whatever database the
connection string in `planvexa-api-secrets` points at — creating that database first if it does not
exist and the login has `CREATEDB` (step 1). Against a genuinely empty database this creates the full
schema (tables, RLS policies, indexes) from scratch — there is no separate "initial schema" step to
run by hand.

`/health/ready` only reports healthy once this has completed, so a rollout that's stuck at
`0/N ready` is very likely still migrating (check `kubectl logs` on the pod) rather than crash-looping.

**Multiple API replicas on first boot:** DbUp takes a Postgres advisory lock
(`PlanvexaDatabase.Upgrade`) before applying scripts, so concurrent replicas starting simultaneously
serialize safely — only one actually runs the scripts, the others wait, then all proceed once the
schema is current. You do not need to scale to 1 replica for a first install, though doing so makes
the (one-time) migration log easier to find.

## 4. First boot: the bootstrap admin and workspace

A schema with no rows in it is not a usable install — nobody can sign in anywhere. So after DbUp, the
API runs a one-time bootstrap (`apps/api/Planvexa.Api/Startup/PlanvexaBootstrap.cs`): if the
configured admin has no workspace yet, it creates one admin user and one workspace, complete with the
five built-in roles, plan entitlements and a starter status scheme / Space / List. It goes through the
same `WorkspaceRegistrationService` path as the product's own "create a workspace" flow, and it
self-skips on every subsequent start.

Configure it in the chart (`api.config.bootstrap` in `values.yaml`):

| Key | Default | Notes |
| --- | --- | --- |
| `Bootstrap__Enabled` | `true` | Set `false` only if you intend to provision the first workspace another way. |
| `Bootstrap__AdminSubject` | `planvexa-admin` | **Must match the `sub` claim of the Keycloak account for this admin.** |
| `Bootstrap__AdminEmail` | `admin@planvexa.example` | |
| `Bootstrap__AdminDisplayName` | `Planvexa Admin` | |
| `Bootstrap__WorkspaceName` | `Planvexa` | |

The application-side user is only half the account — the other half is the Keycloak identity it signs
in with. `scripts/keycloak-bootstrap.ps1` creates the realm, both clients and this admin against any
Keycloak, not just the local dev one:

```bash
pwsh scripts/keycloak-bootstrap.ps1 -BaseUrl https://keycloak.example.com -Realm planvexa -WebOrigin https://app.example.com -IncludeDevelopmentUsers:$false
```

Pass the admin credentials via `KEYCLOAK_ADMIN_USER` / `KEYCLOAK_ADMIN_PASSWORD` and the new account's
password via `BOOTSTRAP_ADMIN_PASSWORD` (without it the account is created with no credential and you
set one in the Keycloak admin console). `-IncludeDevelopmentUsers:$false` is what keeps the four
well-known `*@planvexa.local` dev logins out of a production realm.

Anyone else signs in through Keycloak as normal and is provisioned on first sight; the bootstrap admin
invites them into the workspace, or they create their own.

**Multiple API replicas on first boot:** like DbUp, the bootstrap takes a Postgres advisory lock
(`PlanvexaDatabase.AcquireStartupLockAsync`) and re-checks inside it, so replicas starting together
produce one workspace, not one each.

**Demo data is separate.** `Database__SeedDevelopmentData` (the chart default: `false`) is the
development/CI demo seed — fixed `owner@planvexa.local` / `admin@planvexa.local` / ... accounts with a
well-known password plus sample tasks, chat and documents. Leave it `false` in production. When it
*is* enabled it takes precedence and the bootstrap skips, since the seed already leaves a usable
install behind.

## 5. Verify

- `kubectl get pods` — API and web pods `Running` and `Ready`.
- API: `GET https://<your-host>/health/ready` returns `200` (the Ingress routes `/health` to the API
  Service — see `infrastructure/helm/planvexa/templates/ingress.yaml`).
- Web: `https://<your-host>/login` loads and Keycloak sign-in redirects correctly.
- Take a first backup once real data exists — see `backup-restore.md`.
