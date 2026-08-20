"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { buttonStyles } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { QueryState } from "@/components/ui/QueryState";
import { hostActivityExportHref, listHostActivity } from "@/lib/host/client";
import { hostKeys } from "@/lib/host/queries";
import {
  IsoDateTime,
  PageHeader,
  Pager,
  panelClassName,
  tableHeaderClassName,
  textInputClassName,
  toUtcDateEnd,
  toUtcDateStart,
} from "./host-ui";

const PAGE_SIZE = 50;

export function ActivityPageClient() {
  const [action, setAction] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [skip, setSkip] = useState(0);

  const input = {
    action: action || undefined,
    from: from ? toUtcDateStart(from) : undefined,
    to: to ? toUtcDateEnd(to) : undefined,
    skip,
    take: PAGE_SIZE,
  };
  const activityQuery = useQuery({
    queryKey: hostKeys.activity(input),
    queryFn: () => listHostActivity(input),
  });

  function updateFilter(apply: () => void) {
    apply();
    setSkip(0);
  }

  return (
    <section aria-labelledby="host-activity-title" className="space-y-6">
      <PageHeader
        id="host-activity-title"
        eyebrow="Host administration"
        title="Instance activity"
        description="The audit trail across every workspace, plus instance-level events. Actions taken from this console are recorded with a host. prefix."
      />

      <div className="flex flex-wrap gap-3">
        <label className="sr-only" htmlFor="activity-action">Filter by action</label>
        <input
          id="activity-action"
          type="search"
          value={action}
          onChange={(event) => updateFilter(() => setAction(event.target.value))}
          placeholder="Action contains, e.g. host.workspace"
          className={`${textInputClassName} min-w-56 flex-1`}
        />
        <div className="flex items-center gap-2">
          <label htmlFor="activity-from" className="text-sm text-muted-foreground">From</label>
          <input
            id="activity-from"
            type="date"
            value={from}
            onChange={(event) => updateFilter(() => setFrom(event.target.value))}
            className={textInputClassName}
          />
        </div>
        <div className="flex items-center gap-2">
          <label htmlFor="activity-to" className="text-sm text-muted-foreground">To</label>
          <input
            id="activity-to"
            type="date"
            value={to}
            onChange={(event) => updateFilter(() => setTo(event.target.value))}
            className={textInputClassName}
          />
        </div>
        {/* Exports the same filters currently applied, not just the visible page. Capped server-side
            at 10,000 rows. */}
        <a
          href={hostActivityExportHref(input)}
          className={buttonStyles({ variant: "outline", size: "md" })}
        >
          Export CSV
        </a>
      </div>

      <QueryState query={activityQuery} loadingLabel="Loading activity…">
        {activityQuery.data && activityQuery.data.items.length === 0 ? (
          <EmptyState title="No activity matches" description="Widen the date range, or clear the action filter." />
        ) : activityQuery.data ? (
          <div className={panelClassName}>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[56rem] text-left text-sm">
                <thead className={tableHeaderClassName}>
                  <tr>
                    <th scope="col" className="px-4 py-2 font-semibold">When</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Action</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Entity</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Actor</th>
                    <th scope="col" className="px-4 py-2 font-semibold">Workspace</th>
                    <th scope="col" className="px-4 py-2 font-semibold">IP</th>
                  </tr>
                </thead>
                <tbody>
                  {activityQuery.data.items.map((entry) => (
                    <tr key={entry.id} className="border-t border-border">
                      <td className="whitespace-nowrap px-4 py-2 text-muted-foreground">
                        <IsoDateTime value={entry.createdAtUtc} />
                      </td>
                      <td className="px-4 py-2 font-mono text-xs">{entry.action}</td>
                      <td className="px-4 py-2 text-muted-foreground">{entry.entityType}</td>
                      <td className="px-4 py-2">{entry.actorDisplayName ?? "System"}</td>
                      <td className="px-4 py-2 text-muted-foreground">
                        {/* No workspace = an instance-level event (an account disabled, settings changed). */}
                        {entry.workspaceName ?? "Instance"}
                      </td>
                      <td className="px-4 py-2 font-mono text-xs text-muted-foreground">
                        {entry.ipAddress ?? "—"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pager skip={skip} take={PAGE_SIZE} total={activityQuery.data.total} onChange={setSkip} />
          </div>
        ) : null}
      </QueryState>
    </section>
  );
}
