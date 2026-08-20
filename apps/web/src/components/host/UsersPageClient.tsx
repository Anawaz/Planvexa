"use client";

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { StatusBadge } from "@/components/admin/StatusBadge";
import { EmptyState } from "@/components/ui/EmptyState";
import { QueryState } from "@/components/ui/QueryState";
import { listHostUsers } from "@/lib/host/client";
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

export function UsersPageClient() {
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const [skip, setSkip] = useState(0);

  const input = { search: search || undefined, status: status || undefined, skip, take: PAGE_SIZE };
  const usersQuery = useQuery({ queryKey: hostKeys.users(input), queryFn: () => listHostUsers(input) });

  function updateFilter(apply: () => void) {
    apply();
    setSkip(0);
  }

  return (
    <section aria-labelledby="host-users-title" className="space-y-6">
      <PageHeader
        id="host-users-title"
        eyebrow="Host administration"
        title="Accounts"
        description="Every registered account on this server. Disabling an account blocks its very next request, everywhere — it is not scoped to one workspace."
      />

      <div className="flex flex-wrap gap-3">
        <label className="sr-only" htmlFor="user-search">Search accounts</label>
        <input
          id="user-search"
          type="search"
          value={search}
          onChange={(event) => updateFilter(() => setSearch(event.target.value))}
          placeholder="Name or email"
          className={`${textInputClassName} min-w-56 flex-1`}
        />
        <label className="sr-only" htmlFor="user-status">Status</label>
        <select
          id="user-status"
          value={status}
          onChange={(event) => updateFilter(() => setStatus(event.target.value))}
          className={selectClassName}
        >
          <option value="">All accounts</option>
          <option value="active">Active</option>
          <option value="disabled">Disabled</option>
          <option value="hostadmin">Host administrators</option>
        </select>
      </div>

      <QueryState query={usersQuery} loadingLabel="Loading accounts…">
        {usersQuery.data && usersQuery.data.items.length === 0 ? (
          <EmptyState title="No accounts match" description="Try a different search term, or clear the filter." />
        ) : usersQuery.data ? (
          <div className={panelClassName}>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[52rem] text-left text-sm">
                <thead className={tableHeaderClassName}>
                  <tr>
                    <th scope="col" className="px-4 py-2 font-semibold">Person</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Status</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Workspaces</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Registered</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Last seen</th>
                  </tr>
                </thead>
                <tbody>
                  {usersQuery.data.items.map((user) => (
                    <tr key={user.id} className="border-t border-border">
                      <td className="px-4 py-2">
                        <Link
                          href={`/host/users/${user.id}`}
                          className="font-medium text-primary underline underline-offset-4"
                        >
                          {user.displayName}
                        </Link>
                        <p className="text-xs text-muted-foreground">{user.email}</p>
                      </td>
                      <td className="space-x-2 px-4 py-2">
                        <StatusBadge
                          status={user.isActive ? "Active" : "Disabled"}
                          tone={user.isActive ? "green" : "red"}
                        />
                        {user.isHostAdmin ? <StatusBadge status="Host admin" tone="blue" /> : null}
                        {user.isAnonymized ? <StatusBadge status="Deleted" tone="slate" /> : null}
                      </td>
                      <td className="px-4 py-2 tabular-nums">{user.workspaceCount}</td>
                      <td className="whitespace-nowrap px-4 py-2 text-muted-foreground">
                        <IsoDateTime value={user.createdAtUtc} dateOnly />
                      </td>
                      <td className="whitespace-nowrap px-4 py-2 text-muted-foreground">
                        <IsoDateTime value={user.lastSeenAtUtc} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pager skip={skip} take={PAGE_SIZE} total={usersQuery.data.total} onChange={setSkip} />
          </div>
        ) : null}
      </QueryState>
    </section>
  );
}
