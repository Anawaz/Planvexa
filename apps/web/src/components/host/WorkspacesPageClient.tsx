"use client";

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { EmptyState } from "@/components/ui/EmptyState";
import { QueryState } from "@/components/ui/QueryState";
import { StatusBadge } from "@/components/admin/StatusBadge";
import { listHostWorkspaces } from "@/lib/host/client";
import { hostKeys } from "@/lib/host/queries";
import {
  IsoDateTime,
  PageHeader,
  Pager,
  panelClassName,
  selectClassName,
  tableHeaderClassName,
  textInputClassName,
} from "./host-ui";

const PAGE_SIZE = 25;

export function WorkspacesPageClient() {
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const [skip, setSkip] = useState(0);

  const input = { search: search || undefined, status: status || undefined, skip, take: PAGE_SIZE };
  const workspacesQuery = useQuery({
    queryKey: hostKeys.workspaces(input),
    queryFn: () => listHostWorkspaces(input),
  });

  // Any filter change invalidates the current offset — page 3 of the old result set is meaningless
  // against the new one.
  function updateFilter(apply: () => void) {
    apply();
    setSkip(0);
  }

  return (
    <section aria-labelledby="host-workspaces-title" className="space-y-6">
      <PageHeader
        id="host-workspaces-title"
        eyebrow="Host administration"
        title="Workspaces"
        description="Every workspace in this installation, including ones you are not a member of. Suspending a workspace locks all of its members out; its data is left untouched."
      />

      <div className="flex flex-wrap gap-3">
        <label className="sr-only" htmlFor="workspace-search">Search workspaces</label>
        <input
          id="workspace-search"
          type="search"
          value={search}
          onChange={(event) => updateFilter(() => setSearch(event.target.value))}
          placeholder="Name or slug"
          className={`${textInputClassName} min-w-56 flex-1`}
        />
        <label className="sr-only" htmlFor="workspace-status">Status</label>
        <select
          id="workspace-status"
          value={status}
          onChange={(event) => updateFilter(() => setStatus(event.target.value))}
          className={selectClassName}
        >
          <option value="">All statuses</option>
          <option value="Active">Active</option>
          <option value="Archived">Suspended</option>
        </select>
      </div>

      <QueryState query={workspacesQuery} loadingLabel="Loading workspaces…">
        {workspacesQuery.data && workspacesQuery.data.items.length === 0 ? (
          <EmptyState
            title="No workspaces match"
            description="Try a different search term, or clear the status filter."
          />
        ) : workspacesQuery.data ? (
          <div className={panelClassName}>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[52rem] text-left text-sm">
                <thead className={tableHeaderClassName}>
                  <tr>
                    <th scope="col" className="px-4 py-2 font-semibold">Workspace</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Owner</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Members</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Status</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Created</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Last activity</th>
                  </tr>
                </thead>
                <tbody>
                  {workspacesQuery.data.items.map((workspace) => (
                    <tr key={workspace.id} className="border-t border-border">
                      <td className="px-4 py-2">
                        <Link
                          href={`/host/workspaces/${workspace.id}`}
                          className="font-medium text-primary underline underline-offset-4"
                        >
                          {workspace.name}
                        </Link>
                        <p className="font-mono text-xs text-muted-foreground">{workspace.slug}</p>
                      </td>
                      <td className="px-4 py-2">
                        {workspace.ownerDisplayName ?? "—"}
                        {workspace.ownerEmail ? (
                          <p className="text-xs text-muted-foreground">{workspace.ownerEmail}</p>
                        ) : null}
                      </td>
                      <td className="px-4 py-2 tabular-nums">{workspace.memberCount}</td>
                      <td className="px-4 py-2">
                        {/* "Archived" is the domain status; "Suspended" is what it means here. */}
                        <StatusBadge
                          status={workspace.status === "Archived" ? "Suspended" : workspace.status}
                          tone={workspace.status === "Archived" ? "red" : "green"}
                        />
                      </td>
                      <td className="whitespace-nowrap px-4 py-2 text-muted-foreground">
                        <IsoDateTime value={workspace.createdAtUtc} dateOnly />
                      </td>
                      <td className="whitespace-nowrap px-4 py-2 text-muted-foreground">
                        <IsoDateTime value={workspace.lastActivityAtUtc} fallback="No activity" />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pager skip={skip} take={PAGE_SIZE} total={workspacesQuery.data.total} onChange={setSkip} />
          </div>
        ) : null}
      </QueryState>
    </section>
  );
}
