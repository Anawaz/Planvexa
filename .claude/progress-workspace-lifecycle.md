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
| V1 | `dotnet build Planvexa.slnx -c Release` | done | Build succeeded, 0 warnings (with `-p:NuGetAudit=false`; see note below). |
| V2 | `dotnet test Planvexa.slnx` | done | Architecture 23/23, Unit 655/655, Integration **431/431** — full suite, both `0092` and `0093` in place, nothing excluded. |
| V3 | `npm run lint && npm run build && npm run test` in `apps/web` | done | tsc clean; lint 0 errors (1 pre-existing warning in `FavoritesNav.tsx`, untouched); build ✓ with all 3 new routes; vitest 273/273 in 52 files. |
| V4 | Diff review against the plan | done | Found + fixed by review: A's `0092` insert-ordering (→ `DEFERRABLE`), B's cross-listed-task remap, B's unvalidated `fromStatusId`, and the missing role gate on both status pages (wired to `role >= Admin`, matching `WorkManagementAuthorizer.CanManageStructure`). |
| V5 | Docs update (README + `docs/`) | done | README: cascade-FK rule for new workspace-owned tables + a Statuses/workflows section. Spec: new §6.4 Workspace deletion, §11.1 resolution rules, §11.2 mandatory replacement + known ceiling. |

## Known issue, pre-existing and out of scope

`dotnet build Planvexa.slnx -c Release` fails on **NU1903** — SSH.NET 2025.1.0 has a known
high-severity advisory and arrives transitively via Testcontainers, with warnings-as-errors on.
Confirmed to fail identically on a clean stashed tree, so it is not from this branch. All builds here
used `-p:NuGetAudit=false`. Fixing it means bumping a dependency and belongs on its own branch.
