# AGENTS.md — Planvexa AI Agent Execution Protocol

Planvexa is a workspace-centric, full-featured task-management SaaS built as a **modular monolith**
(.NET 10 backend, PostgreSQL 18, Next.js 16 frontend). This file is binding for every AI coding
agent that works in this repository.

**Product model (authoritative):**
There is no Organization/Tenant layer. `User` is a global identity; a user may belong to many
**Workspaces** with an independent role in each and can switch Workspaces without re-authenticating.
**Workspace is the single top-level business, authorization, billing, configuration, and
feature-entitlement boundary.** There is no Organization/Tenant layer in the target product; the legacy
Tenant layer (`Tenant`, `ITenantOwned`, `ITenantContext`, `tenant_id` columns, tenant-keyed RLS
policies) has been fully removed — Workspace is the sole isolation boundary end to end.

## Mandatory rules

1. Keep continuous, verified progress. Finish the vertical slice you are on, then continue
   automatically into the next one once every exit criterion passes — do not pause for confirmation
   between tasks.
2. Do not start dependent work before the slice it depends on is complete and green.
3. Understand existing architecture decisions before modifying code.
4. Every Workspace-owned entity must contain a non-nullable `WorkspaceId` (UUIDv7). Global identity
   tables (Users, identity-provider links, truly global user preferences/sessions) are not
   Workspace-owned and must not receive a `WorkspaceId`.
5. Never trust a `WorkspaceId` supplied in a request body or an unvalidated header. Resolve it from
   server-side context (authenticated user + validated Workspace membership).
6. Every endpoint must have explicit authentication and authorization behavior. Only bootstrap
   endpoints (`GET /api/v1/users/me`, `GET /api/v1/workspaces/me`) may run before a Workspace is
   selected.
7. Every cross-module dependency must use an approved contract (`SharedContracts`) or domain event.
8. Every schema change requires an ordered DbUp SQL script in `src/Database/Planvexa.Database/Scripts`.
9. Every database script must consider existing Workspace data (empty DB **and** upgraded DB) and be validated through DbUp tests.
10. Every public endpoint requires integration tests.
11. Every permission-sensitive endpoint requires negative (cross-Workspace / unauthorized) tests.
12. Every important state change requires an audit event.
13. Every external side effect must be idempotent.
14. No secrets may be stored in source control.
15. Do not add a package without a documented licence and purpose.
16. Prefer existing framework capabilities over unnecessary dependencies.
17. Build the complete solution and run automated tests after changes.
18. Do not start the application for manual execution unless explicitly instructed.
19. Do not suppress failing tests to call work complete.
20. Update documentation before declaring work complete.

## Architecture invariants

- **Modular monolith.** Modules live under `src/Modules/*`. A module must not read or write another
  module's tables directly. Communicate through `SharedContracts` or domain events. Architecture
  tests in `tests/Architecture` enforce boundaries.
- **Workspace isolation.** `WorkspaceId NOT NULL` + composite indexes beginning with `WorkspaceId` +
  composite foreign keys for critical child tables + PostgreSQL Row-Level Security (keyed on
  `app.current_workspace_id`) + query filters. The normal application database role is a
  non-superuser role without `BYPASSRLS`; migration/maintenance use separate roles.
- **Identity vs authorization.** Keycloak proves identity (one realm per environment). `User` is a
  global directory record. The application database owns Workspace membership, roles, guest access,
  resource permissions and subscription entitlements — all scoped per Workspace.
- **Outbox.** State changes and their domain events are written in the same transaction; a worker
  publishes them. NATS JetStream is introduced only when cross-process distribution is required.
- **IDs.** UUIDv7 via `Guid.CreateVersion7()` for sortable globally-unique identifiers.
- **Money.** Always decimal arithmetic; never floating point.
- **Time.** Store UTC + the user's IANA timezone; compute local day boundaries with the timezone.

## Change workflow

### Step 1 — Read
`AGENTS.md`, the relevant module code, and any architecture decisions that bear on the change.

### Step 2 — Plan
Scope out-of-scope, domain/database/API/UI changes, security considerations, tests, migration
risks, and rollback for the slice.

### Step 3 — Implement vertically
Implement complete slices (domain command → validation → authorization → migration → repository →
API endpoint → audit event → frontend mutation → UI → unit/integration/E2E tests), not horizontal
layers.

### Step 4 — Validate
Run: format checks, compilation, unit tests, architecture tests, integration tests, frontend tests,
E2E (when in scope), dependency scan, migration validation.

### Step 5 — Continue at the gate
A slice is complete only when every exit criterion passes (build clean, tests green, isolation intact).
Once it does, continue automatically into the next one. Only stop when the environment genuinely
lacks the capability to proceed, after completing every task that remains possible.

## Local commands

```bash
# Backend
dotnet build Planvexa.slnx -c Release          # build everything (warnings are errors)
dotnet test  Planvexa.slnx                      # unit + architecture + integration
dotnet test tests/Integration/Planvexa.IntegrationTests/Planvexa.IntegrationTests.csproj -c Release --filter DbUp`r`n
# Dev infrastructure
pwsh scripts/dev-up.ps1                         # start Keycloak/Mailpit/Jaeger
pwsh scripts/dev-down.ps1

# Frontend
cd apps/web && npm ci && npm run lint && npm run build
```

## Repository map

| Path | Purpose |
| --- | --- |
| `apps/api` | ASP.NET Core host (composition root, endpoints, middleware) |
| `apps/web` | Next.js web client |
| `apps/worker` | Background worker host (outbox/automations — reserved, currently empty) |
| `apps/collaboration` | Realtime collaboration host (Node/TypeScript Hocuspocus server) |
| `src/BuildingBlocks` | Shared kernel (no external module deps) |
| `src/SharedContracts` | Cross-module integration contracts |
| `src/Modules/*` | Bounded contexts (Tenancy, Identity, Audit, …) |
| `src/Infrastructure` | Persistence (DbContext, migrations), outbox, cross-cutting infra |
| `tests/*` | Unit, Integration, Architecture, EndToEnd, Security, Performance |
| `infrastructure/*` | docker, helm, opentofu, argocd |


