# Contributing to Planvexa

Thanks for considering a contribution. Planvexa is a self-hosted, open-source work-management
platform — see [`README.md`](README.md) for the tech stack and local setup, and
[`AGENTS.md`](AGENTS.md) for the architectural rules every change must follow (module boundaries,
Workspace isolation, DbUp conventions). Read `AGENTS.md` before touching backend code; it is binding,
not optional guidance.

## Before you start

- Check open issues and pull requests to avoid duplicate work.
- For anything larger than a small fix (a new module, a schema change, a behavior change to
  authorization or Workspace isolation), open an issue first to discuss the approach.
- Small, focused pull requests are much easier to review than large ones. Prefer several small PRs
  over one that touches many unrelated things.

## Local setup

Follow [`README.md`](README.md#getting-started-local-development) — it covers prerequisites,
starting the stack via the Aspire AppHost or `scripts/dev-up.ps1`, and seeded development accounts.
In short:

```powershell
dotnet build Planvexa.slnx -c Release   # backend build, warnings are errors
dotnet test  Planvexa.slnx              # unit + architecture + integration (needs Docker)

cd apps/web
npm ci
npm run lint
npm run typecheck
npm run test
npm run build
```

## Making a change

1. **Backend module boundaries are enforced by architecture tests** (`tests/Architecture`). Modules
   under `src/Modules/*` never reference another module's tables or internals directly — cross-module
   dependencies go through `src/SharedContracts`. If your change needs data from another module,
   look for an existing contract there before adding a new dependency.
2. **Every schema change is a new, ordered DbUp script** in
   `src/Database/Planvexa.Database/Scripts/`, following the existing `NNNN_Description.sql` naming
   and the Row-Level Security pattern already used by neighboring scripts (`workspace_isolation`
   policies, `FORCE ROW LEVEL SECURITY`). Never edit a script that may already be applied
   elsewhere — add a new one.
3. **Workspace is the only business-data isolation boundary.** New entities that belong to a
   Workspace must implement the `IWorkspaceOwned` marker, get a non-nullable `WorkspaceId`, and go
   through the standard query-filter/RLS pattern.
4. **No mocks, hard-coded IDs, or stubbed handlers presented as complete.** If something is
   genuinely out of scope for your change, leave it alone rather than half-implementing it.
5. **Add or update tests with the change**, not as a follow-up. Backend: unit tests for domain logic,
   integration tests for API/DB behavior, negative tests for anything permission-sensitive (a change
   that touches authorization should include a test proving the *denied* case, not just the allowed
   one). Frontend: component tests for non-trivial UI logic, Playwright coverage for new user-facing
   flows.

## Commit and PR conventions

- Write commit messages that explain *why*, not just *what* — the diff already shows what changed.
- Keep the backend build warning-free (`-c Release` treats warnings as errors) and the frontend
  `lint`/`typecheck` clean before opening a PR.
- Describe what you tested and how in the PR description (which test suites you ran, whether you
  exercised the change manually against the running app).
- CI (`.github/workflows/ci.yml`) runs backend build/test, frontend lint/typecheck/test/build,
  Playwright e2e, a Docker image build, and a dependency vulnerability scan. All must pass.

## Reporting bugs

Open a GitHub issue with: what you did, what you expected, what happened instead, and enough detail
to reproduce it (Planvexa version/commit, browser if frontend, relevant logs). For security
vulnerabilities, do **not** open a public issue — see [`SECURITY.md`](SECURITY.md).

## Code of conduct

Be respectful and constructive. Disagreements about technical approach are normal and welcome;
personal attacks are not.
