"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { QueryState } from "@/components/ui/QueryState";
import { getHostOverview } from "@/lib/host/client";
import { hostKeys } from "@/lib/host/queries";
import type { HostMonthlyCount } from "@/lib/host/types";
import { IsoDateTime, PageHeader, StatTile, panelClassName, tableHeaderClassName } from "./host-ui";

const monthLabel = new Intl.DateTimeFormat("en", { month: "short" });

function label(bucket: HostMonthlyCount) {
  return monthLabel.format(new Date(Date.UTC(bucket.year, bucket.month - 1, 1)));
}

/**
 * Twelve months of workspace creation as a plain bar row — CSS heights over a charting dependency.
 * A dozen values with no axes, tooltips or interaction does not justify one.
 */
function CreationTrend({ buckets }: { buckets: HostMonthlyCount[] }) {
  const peak = Math.max(1, ...buckets.map((bucket) => bucket.count));

  return (
    <div className={`${panelClassName} p-4`}>
      <h2 className="text-sm font-semibold">Workspaces created</h2>
      <p className="mt-1 text-xs text-muted-foreground">Last 12 months</p>
      {buckets.length === 0 ? (
        <p className="mt-4 text-sm text-muted-foreground">No workspaces created in this window.</p>
      ) : (
        <ul className="mt-4 flex h-32 items-end gap-2">
          {buckets.map((bucket) => (
            <li key={`${bucket.year}-${bucket.month}`} className="flex flex-1 flex-col items-center gap-1">
              <div
                className="w-full rounded-t bg-primary/70"
                style={{ height: `${Math.max(4, (bucket.count / peak) * 100)}%` }}
                // The bar is decorative; the number and month below carry the same information in text.
                aria-hidden="true"
              />
              <span className="text-xs tabular-nums text-muted-foreground">{bucket.count}</span>
              <span className="text-[0.6875rem] text-muted-foreground">{label(bucket)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function OverviewPageClient() {
  const overviewQuery = useQuery({ queryKey: hostKeys.overview(), queryFn: getHostOverview });

  return (
    <section aria-labelledby="host-overview-title" className="space-y-6">
      <PageHeader
        id="host-overview-title"
        eyebrow="Host administration"
        title="Instance overview"
        description="Everything running in this Planvexa installation. Workspace administrators continue to manage only their own workspaces."
      />

      <QueryState query={overviewQuery} loadingLabel="Loading instance overview…">
        {overviewQuery.data ? (
          <div className="space-y-6">
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <StatTile
                label="Active workspaces"
                value={overviewQuery.data.activeWorkspaces}
                hint={`${overviewQuery.data.archivedWorkspaces} suspended`}
              />
              <StatTile
                label="Active accounts"
                value={overviewQuery.data.activeUsers}
                hint={`${overviewQuery.data.disabledUsers} disabled`}
              />
              <StatTile
                label="Memberships"
                value={overviewQuery.data.memberships}
                hint="Across all workspaces"
              />
              <StatTile
                label="Host administrators"
                value={overviewQuery.data.hostAdmins}
                hint="Can reach this console"
              />
              <StatTile label="Seen in last 7 days" value={overviewQuery.data.usersSeenLast7Days} />
              <StatTile label="Seen in last 30 days" value={overviewQuery.data.usersSeenLast30Days} />
            </div>

            <CreationTrend buckets={overviewQuery.data.workspacesCreatedByMonth} />

            <div className={panelClassName}>
              <div className="flex flex-wrap items-center justify-between gap-3 p-4">
                <h2 className="text-sm font-semibold">Recent activity</h2>
                <Link href="/host/activity" className="text-sm font-medium text-primary underline underline-offset-4">
                  View all activity
                </Link>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full min-w-[46rem] text-left text-sm">
                  <thead className={tableHeaderClassName}>
                    <tr>
                      <th scope="col" className="px-4 py-2 font-semibold">When</th>
                      <th scope="col" className="px-4 py-2 font-semibold">Action</th>
                      <th scope="col" className="px-4 py-2 font-semibold">Actor</th>
                      <th scope="col" className="px-4 py-2 font-semibold">Workspace</th>
                    </tr>
                  </thead>
                  <tbody>
                    {overviewQuery.data.recentActivity.map((entry) => (
                      <tr key={entry.id} className="border-t border-border">
                        <td className="whitespace-nowrap px-4 py-2 text-muted-foreground">
                          <IsoDateTime value={entry.createdAtUtc} />
                        </td>
                        <td className="px-4 py-2 font-mono text-xs">{entry.action}</td>
                        <td className="px-4 py-2">{entry.actorDisplayName ?? "System"}</td>
                        <td className="px-4 py-2 text-muted-foreground">{entry.workspaceName ?? "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        ) : null}
      </QueryState>
    </section>
  );
}
