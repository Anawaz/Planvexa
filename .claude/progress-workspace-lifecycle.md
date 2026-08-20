# Progress ledger — feat/workspace-lifecycle-and-status-settings

Plan: `C:\Users\an\.claude\plans\you-are-opus-plan-binary-leaf.md`
Branch: `feat/workspace-lifecycle-and-status-settings` (cut from `main` @ 9c8407a)

Agents update this file after each step so an interruption, error, or usage-limit reset resumes from
the last completed step instead of restarting. Status values: `todo` / `in-progress` / `done` /
`blocked`.

## Workstream C — small fixes + nav wiring (Sonnet)

| Step | Description | Status | Notes |
| --- | --- | --- | --- |
| C1 | Topbar stale display name (`Topbar.tsx:36`) | done | |
| C2 | Security page toggle layout fix + tidy | done | |
| C3 | `nav-config.ts` entries for `/app/settings/workspace` and `/app/settings/statuses` | done | |

## Workstream A — hard-delete workspace + create (Opus)

| Step | Description | Status | Notes |
| --- | --- | --- | --- |
| A1 | DbUp `0092_AddWorkspaceCascadeForeignKeys.sql` | done | FKs are `DEFERRABLE INITIALLY DEFERRED NOT VALID` (EF has no cross-module dependency edge, so statement-order checking is unsafe). Also adds the missing `workspace_self_delete` DELETE policy on `tenancy.workspaces` — 0029 never gave it one, so every DELETE was silently filtered to 0 rows. |
| A2 | `IFileStorage.DeletePrefixAsync` + local/S3 impls | done | Local disk reuses `Resolve`; S3 paginates `ListObjectsV2` + batched `DeleteObjects`. Both `FakeFileStorage` doubles updated. |
| A3 | `WorkspaceDeletionService` + `POST /workspaces/{id}/delete` | done | Raw delete goes through new `IWorkspaceStore.DeleteCascadeAsync` (Infrastructure owns the DbContext); reuses `TenancySessionGuard` for the GUC. |
| A4 | `/app/settings/workspace` page, switcher `+`, onboarding query-key fix | done | |
| A5 | `WorkspaceDeletionFlowTests` | done | 4/4 green. Full integration suite 429/429. Two pre-existing tests seeded synthetic `workspace_id`s with no parent row and needed real workspaces after 0092: `DbUpMigrationTests.ChildWorkspaceColumns_AreBackfilledAndNotNull` and `WorkManagementIsolationTests.Workspace_rls_isolates_rows_between_independent_workspaces`. |

## Workstream B — workspace-default + space-override statuses (Opus)

| Step | Description | Status | Notes |
| --- | --- | --- | --- |
| B1 | `Space.StatusSchemeId` + `StatusScheme.SpaceId` + DbUp `0093` | done | `0093_AddSpaceStatusSchemes.sql`. `FindDefaultAsync` now filters `SpaceId == null`. `SpaceConfiguration` declares the Space→StatusScheme FK for EF save ordering. |
| B3 | Domain: Rename/UpdateStatus/RemoveStatus/MoveStatus/CloneFor | done | All on `StatusScheme`; `RemoveStatus` scrubs the id from other statuses' `AllowedNextStatusIds`. |
| B4 | Store additions (scheme / work-item / list) | done | `ListByWorkspaceAsync` gained a `workspaceLevelOnly` optional param instead of a sibling method. |
| B5 | `StatusSchemeService` methods incl. reassignment + space customize/reset | done | Remap always goes through `WorkItem.ChangeStatus`. Preset-based customize maps existing tasks to the new scheme's `DefaultStatus()`. Review fixes: customize skips cross-listed tasks whose primary list is elsewhere (`ListByListAsync` is membership-driven); reset rejects a `fromStatusId` outside the Space scheme; `AnyBySchemeAsync` dropped in favour of one `ListBySchemeAsync` count. |
| B6 | Endpoints (status CRUD + space status-scheme) | done | DELETE-with-body works, but the body parameter needs an explicit `[FromBody]` — minimal APIs refuse to *infer* one on DELETE. |
| B7 | Frontend: `StatusSchemeEditor`, `/app/settings/statuses`, `/app/spaces/[id]/statuses`, presets | done | `apiClient.delete` gained a `body` param (DELETE-with-body). `listStatusSchemes` kept as-is + new sibling `listWorkspaceStatusSchemes()` — adding an optional arg breaks the bare `queryFn: listStatusSchemes` call sites, since TanStack passes the query context as arg 1. `StatusScheme` gained required `isDefault`/`spaceId`, so 2 test fixtures were updated. tsc clean, lint clean (1 pre-existing warning in `FavoritesNav.tsx`), build OK, 29 tests green incl. new `StatusSchemeEditor.test.tsx`. |
| B8 | `StatusSchemeManagementTests` | done | 15 tests, all green (2 added for the review defects: cross-listed task untouched by customize, foreign `fromStatusId` on reset rejected). Full integration suite 428/429 before those 2 — the one failure is A5's `WorkspaceDeletionFlowTests`, which needs A1's `0092`. |

## Final review (Opus, orchestrator)

| Step | Description | Status | Notes |
| --- | --- | --- | --- |
| V1 | `dotnet build Planvexa.slnx -c Release` | done | Build succeeded, 0 warnings, **no audit flag needed** (NU1903 fixed, see below). |
| V2 | `dotnet test Planvexa.slnx` | done | Architecture 23/23, Unit 655/655, Integration **431/431** — full suite, both `0092` and `0093` in place, nothing excluded. Re-run after the Testcontainers bump: same result. |
| V6 | NU1903 (SSH.NET advisory) | done | Testcontainers.PostgreSql + .Minio 4.13.0 → 4.14.0, which pulls the patched SSH.NET 2026.0.0. |
| V3 | `npm run lint && npm run build && npm run test` in `apps/web` | done | tsc clean; lint 0 errors (1 pre-existing warning in `FavoritesNav.tsx`, untouched); build ✓ with all 3 new routes; vitest 273/273 in 52 files. |
| V4 | Diff review against the plan | done | Found + fixed by review: A's `0092` insert-ordering (→ `DEFERRABLE`), B's cross-listed-task remap, B's unvalidated `fromStatusId`, and the missing role gate on both status pages (wired to `role >= Admin`, matching `WorkManagementAuthorizer.CanManageStructure`). |
| V5 | Docs update (README + `docs/`) | done | README: cascade-FK rule for new workspace-owned tables + a Statuses/workflows section. Spec: new §6.4 Workspace deletion, §11.1 resolution rules, §11.2 mandatory replacement + known ceiling. |

## Follow-up round: poisoned dev cache + whole-app browser sweep

| Step | Description | Status | Notes |
| --- | --- | --- | --- |
| F1 | Prevent the `.next` collision | done | `distDir: process.env.NEXT_DIST_DIR ?? ".next"` in `next.config.ts`. Proven: after `NEXT_DIST_DIR=.next-verify npm run build`, `.next/dev/types/routes.d.ts` is byte-identical and `BUILD_ID` lands in `.next-verify`. Deployment path (`output: "standalone"`) unchanged. |
| F2 | Heal an already-poisoned cache | done | Guard in `web-install` (`AppHost.cs`), which `web` already `.WaitForCompletion`s. Keys on `.next/BUILD_ID`. Proven both ways in a scratch dir: warm dev cache left alone, `BUILD_ID` present → `.next` removed. |
| F3 | Failed load must not look empty | done | `/app/settings/statuses` and `/app/settings/workflows` now render through the existing `QueryState`. They previously did `data ?? []`, so an API failure showed "No workflows yet." and hid every editor — which is what made this look like a missing feature. |
| F4 | Whole-app route smoke sweep | done | New `e2e/app-smoke.spec.ts`, derived from `navigation` in `nav-config.ts` so new pages are covered automatically. 45 routes + task detail panel. Asserts no error boundary, a level-1 heading, and (via console-guard) no console errors. |
| F5 | Triage sweep findings | done | **No pre-existing app bugs found — all 45 routes passed on the first run.** The only failures were my own: a wrong task-panel assertion, and two over-broad `getByRole("alert")` checks that matched the Next.js dev-tools overlay. |
| F6 | Fix the `tasks.spec.ts` flake | done | Root cause: assertions raced the post-reload refetch. Now waits for the row before asserting on its controls. 3/3 clean in isolation, 2 consecutive full suites clean. |
| F7 | Stop test data accumulating | done | `global-teardown.ts` now also drains `E2E *` scratch workflows and `E2E Throwaway*` workspaces. 39 schemes had piled up from failed runs; teardown removed 42 on first exercise. Also stripped two stray `E2E Renamed` statuses my earliest specs left in the seeded default scheme. |
| F8 | Final verification | done | e2e **74 passed, 0 flaky**, twice consecutively. `dotnet build Planvexa.slnx -c Release` 0 warnings. tsc/lint clean. Security, statuses and space-statuses pages confirmed visually in dark theme. |

## NU1903 (fixed here at the user's request)

`dotnet build -c Release` used to fail on **NU1903**: SSH.NET 2025.1.0 carries
[GHSA-q939-rpr3-3284](https://github.com/advisories/GHSA-q939-rpr3-3284) (high severity) and arrived
transitively through Testcontainers, with warnings-as-errors on. It predated this branch — it failed
identically on a clean tree.

Fixed by bumping `Testcontainers.PostgreSql` and `Testcontainers.Minio` from 4.13.0 to 4.14.0 in
`Directory.Packages.props`; that version references the patched SSH.NET 2026.0.0. No direct
dependency on SSH.NET was added, so nothing new needs a licence/purpose entry (AGENTS.md rule 15).
`dotnet build Planvexa.slnx -c Release` now succeeds with 0 warnings and no `NuGetAudit` override,
and the full suite still passes.
