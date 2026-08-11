"use client";

import { useAppContext } from "@/lib/app-context/AppContext";

export function WorkspaceSwitcher() {
  const { workspaces, currentWorkspace, setCurrentWorkspaceId, isLoading } = useAppContext();

  if (isLoading) {
    return <span className="text-sm text-muted-foreground">Loading workspace…</span>;
  }

  return (
    // min-w-0 all the way down: without it the select refuses to shrink below its widest option
    // and pushes the whole topbar past the viewport.
    <div className="flex min-w-0 items-center gap-2 text-sm font-medium">
      <label className="flex min-w-0 items-center gap-2">
        <span className="hidden shrink-0 text-muted-foreground sm:inline">Workspace</span>
        <select
          className="min-w-0 max-w-56 rounded-lg border border-border bg-card px-3 py-2 text-sm shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          value={currentWorkspace?.id ?? ""}
          aria-label="Current workspace"
          onChange={(event) => setCurrentWorkspaceId(event.currentTarget.value)}
        >
          {workspaces.map((workspace) => (
            <option key={workspace.id} value={workspace.id}>{workspace.name}</option>
          ))}
        </select>
      </label>
    </div>
  );
}
