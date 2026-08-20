# Host administration

The **host administration console** at `/host` is the instance-level management surface for whoever
runs a Planvexa server. It is separate from Workspace administration (`/app/settings/*`), which stays
scoped to a single Workspace and is reached through a Workspace role.

A host administrator manages the *installation*. A Workspace Owner or Admin manages *their Workspace*.
Neither role implies the other, and a host administrator is typically a member of no Workspace at all.

## What the console does

| Section | What it shows / does |
| --- | --- |
| Overview | Workspace, account and membership counts; 7/30-day activity; 12-month workspace-creation trend; recent audit events |
| Workspaces | Every Workspace on the server (including ones you do not belong to) with owner, member count, status and last activity; suspend, restore, delete |
| Accounts | Every registered account with status, membership count and last seen; disable, enable, grant/revoke host administration |
| Activity | The audit trail across all Workspaces plus instance-level events |
| Logs | Warnings and errors captured from this server (see [Logs and privacy](#logs-and-privacy)) |
| Health | Database reachability and version, DbUp schema version, outbox backlog, error counts, storage/mail/maintenance configuration |
| Settings | Self-registration, who may create Workspaces, instance branding and support contact |

## What the console deliberately does NOT do

**Host administration is metadata-only.** No endpoint under `/api/v1/host/*` returns task titles,
document bodies, comments, chat messages or attachment contents — only counts, sizes and timestamps.
Workspace remains the isolation boundary for content.

There is also **no impersonation**: a host administrator cannot enter a Workspace or act as one of its
members. To work inside a Workspace they must be invited to it like anyone else, at which point they
appear in that Workspace's member list and audit trail as themselves.

If you need one of these capabilities, it is a deliberate product decision to add — not an oversight.

## Who is a host administrator

Host administration is a flag on the global account (`identity.users.is_host_admin`), not a Workspace
role. There are three ways it gets set:

1. **First run.** `PlanvexaBootstrap` grants it to the account identified by `Bootstrap:AdminSubject`
   **if the installation has no active host administrator yet**. This runs on every start, so an
   existing installation upgrading into this feature gets its first host administrator on the next
   restart without any manual step.
2. **From the console.** An existing host administrator promotes another account on its detail page.
3. **Break-glass configuration.** See below.

Once at least one host administrator exists, the bootstrap never grants again — so demoting the
bootstrap account is permanent, not undone by the next restart.

Two guards prevent an installation from locking itself out entirely. Neither can be overridden from
the console:

- You cannot disable or demote **yourself**.
- You cannot disable or demote the **last remaining** host administrator.

## Recovering from a lockout

If every host administrator account is lost (deleted, or its identity-provider account is gone), add
the identity-provider subject to `HostAdmin:Subjects` and restart:

```json
{
  "HostAdmin": {
    "Subjects": ["the-idp-subject-of-the-rescue-account"]
  }
}
```

or, in a container:

```bash
HostAdmin__Subjects=the-idp-subject-of-the-rescue-account
```

A subject listed here reaches the console regardless of the database flag. Use it to sign in, grant
host administration to a real account through the console, then remove the entry and restart. It is
empty by default; setting it requires filesystem or environment access to the server, which is the
same trust level as the database itself.

Note this is the **identity-provider subject** (Keycloak's `sub` claim), not an email address.

## Row-level security

The console reads across every Workspace, which PostgreSQL row-level security otherwise forbids:
`tenancy.workspaces` and `tenancy.workspace_members` are `FORCE ROW LEVEL SECURITY` and their existing
policies require active membership in the Workspace being read.

Script `0094_AddHostAdministration.sql` adds `host_admin_read` / `host_admin_update` policies keyed on
the `app.current_user` session variable and re-validated against `identity.users.is_host_admin` inside
the database. That means:

- **The console does not require the `planvexa_maint` (`BYPASSRLS`) role.** It works on every install,
  configured or not. The maintenance connection remains what it always was: for cross-Workspace
  background sweeps.
- **The database enforces host administration too**, not just the application's authorization policy.
  Revoking the flag closes the door at both layers on the caller's very next request.

## Suspending vs deleting a Workspace

**Suspend** sets the Workspace's status to `Archived`, which `WorkspaceResolver` already refuses to
resolve — every member is locked out on their next request. Nothing is deleted and it is reversible at
any time.

**Delete** is irreversible. It cascades to every Workspace-owned table and sweeps the Workspace's blob
storage, and requires the Workspace slug to be retyped. It uses the same implementation as the
Owner-facing delete. The audit event is written and committed *before* the deletion, so the record
outlives the Workspace it describes.

## Disabling an account

Disabling clears `identity.users.is_active`. Enforcement is in `UserDirectory.GetOrProvisionAsync`,
the single path every authenticated HTTP request and every SignalR connection passes through — so the
account is blocked everywhere on its very next request, not per-endpoint and not at the next token
refresh. Their data and Workspace memberships are untouched, and enabling restores access.

Accounts deleted through the self-service account-deletion flow are anonymized and cannot be
re-enabled: their personal data is gone, so there is nothing to restore.

## Instance settings

| Setting | Effect |
| --- | --- |
| Allow self-registration | When off, only someone with a pending Workspace invitation can create an account. Existing accounts are unaffected. |
| Who may create workspaces | `Anyone` or `HostAdminsOnly`. Enforced in `WorkspaceRegistrationService`, the single Workspace-creation path. Existing Workspaces are unaffected. |
| Instance name / logo / support email | Shown on the sign-in page before anyone has a session, via the anonymous `GET /api/v1/public/registration-policy`. |

`Registration:AllowSelfRegistration` in configuration is now only the **seed default**. On first read,
`InstanceSettingsService` creates the `platform.instance_settings` row using whatever that key says —
so an installation that had self-registration switched off keeps it switched off through the upgrade.
After that the row owns the value and editing the configuration key has no further effect.

## Logs and privacy

The Logs page reads `platform.instance_logs`, filled by an `ILoggerProvider` that queues records for a
background writer.

- **Minimum level defaults to `Warning`** (`InstanceLogs:MinimumLevel`). Log messages are whatever the
  application logged and can contain user data, so this store deliberately captures problems rather
  than everything.
- **Retention defaults to 14 days** (`InstanceLogs:RetentionDays`), swept hourly. This is what bounds
  how long any logged user data lives in the database. Raise it deliberately.
- **Logging never blocks a request.** The queue is bounded (`InstanceLogs:Capacity`, default 2000); a
  burst that outruns the writer is dropped, and the dropped count is reported on the Health page so a
  truncated log is visible rather than looking like a quiet system.
- Set `InstanceLogs:Enabled=false` to capture nothing.

This store is **not** a replacement for the OpenTelemetry → Loki/Grafana pipeline, which remains the
system of record for full-fidelity logs across replicas and far longer retention. The console's log
view exists so an operator can answer "what broke on this box just now?" without shell access.

## Auditing

Every host action writes an audit event with a `host.` action prefix — `host.workspace.suspended`,
`host.user.disabled`, `host.user.host_admin_granted`, `host.settings.updated` and so on. They are
visible in the console's Activity page and, for Workspace-targeted actions, in that Workspace's own
audit log too:

- **Workspace-targeted** actions (suspend/restore/delete) carry the target `WorkspaceId`, so the
  Workspace's own owners can see that it happened.
- **Account- and instance-targeted** actions carry a null `WorkspaceId` — the documented meaning of
  that column for platform-level events.

## Related

- `apps/api/Planvexa.Api/Auth/HostAdminPolicy.cs` — the authorization policy and break-glass parsing
- `src/Infrastructure/Planvexa.Infrastructure/HostAdmin/` — cross-Workspace queries and actions
- `src/Database/Planvexa.Database/Scripts/0094_AddHostAdministration.sql` — the flag and RLS policies
- `tests/Integration/Planvexa.IntegrationTests/HostAdminFlowTests.cs` — the behaviour this documents,
  proven against a real database under a non-superuser role
