## What this changes and why

<!-- The "why" matters more than the "what" — the diff already shows what changed. -->

## How this was tested

- [ ] `dotnet build Planvexa.slnx -c Release` — 0 warnings, 0 errors
- [ ] `dotnet test Planvexa.slnx` — unit + architecture + integration pass
- [ ] Frontend `npm run lint && npm run typecheck && npm run test && npm run build` pass (if touched)
- [ ] Manually exercised the change against the running app (describe how)
- [ ] Added/updated tests for the new behavior, including negative tests for any authorization change

## Database changes

<!-- If this touches src/Database/Planvexa.Database/Scripts: confirm it's a new, ordered,
     forward-only script (never edit an already-applied one), and that it's safe on both a blank
     database and an already-upgraded one. Delete this section if there's no schema change. -->

## Checklist

- [ ] Follows the module-boundary rules in [AGENTS.md](../AGENTS.md) (no direct cross-module table access)
- [ ] Workspace-owned entities implement `IWorkspaceOwned` with a non-nullable `WorkspaceId`
- [ ] No mocks, hard-coded IDs, or stubbed handlers presented as complete
