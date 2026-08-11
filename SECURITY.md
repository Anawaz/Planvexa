# Security Policy

## Reporting a vulnerability

**Do not open a public GitHub issue for a security vulnerability.** Public issues are visible to
everyone, including anyone who might exploit the report before a fix ships.

Instead, report it privately through GitHub's Security Advisories for this repository:
**[Report a vulnerability](../../security/advisories/new)** (Security tab → "Report a vulnerability").
This opens a private draft advisory visible only to you and the maintainers.

Include, as far as you're able to:

- A description of the vulnerability and its impact (what an attacker could do).
- Steps to reproduce, or a proof-of-concept.
- The affected version/commit.
- Whether you're aware of it being actively exploited.

We aim to acknowledge new reports within 5 business days and to keep you updated as we investigate
and fix the issue. Please give us reasonable time to ship a fix before any public disclosure.

## Supported versions

Planvexa does not yet have a formal release/version numbering scheme; security fixes land on `main`.
Self-hosted deployments should track `main` (or the latest tagged release, once tagging begins) and
apply updates promptly.

## Scope

In scope:

- The Planvexa backend (`apps/api`, `src/`), frontend (`apps/web`), and database schema/migrations
  (`src/Database`).
- Authentication, authorization, and Workspace-isolation logic — this is the highest-priority area
  (cross-workspace access, authorization bypass, injection, and leakage through
  public/externally-delivered features are the priority risk categories).
- Deployment artifacts under `infrastructure/` (Docker, Helm, OpenTofu) as shipped in this repository.

Out of scope:

- Vulnerabilities in third-party dependencies — please report those upstream (and feel free to also
  let us know so we can track an update). Dependency vulnerability scanning already runs in CI
  (`.github/workflows/ci.yml`).
- Issues that require an already-compromised administrator account or physical access to a
  self-hosted deployment's infrastructure.
- Findings from automated scanners without a demonstrated, concrete impact.

## Self-hosting security notes

Planvexa is designed to be self-hosted. A few things every deployment should get right:

- Run the application database role as a non-superuser without `BYPASSRLS` — Row-Level Security is a
  real isolation boundary here, not defense-in-depth theater.
- Never expose the PostgreSQL port, Keycloak admin console, or internal service ports to the public
  internet.
- Rotate the `PlanvexaMaintenance` connection credentials and any integration/webhook secrets on a
  regular schedule.
- Keep Keycloak and PostgreSQL patched independently of Planvexa application updates.
