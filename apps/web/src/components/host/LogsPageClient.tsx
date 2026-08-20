"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { EmptyState } from "@/components/ui/EmptyState";
import { QueryState } from "@/components/ui/QueryState";
import { StatusBadge } from "@/components/admin/StatusBadge";
import { listInstanceLogs } from "@/lib/host/client";
import { hostKeys } from "@/lib/host/queries";
import type { InstanceLogEntry } from "@/lib/host/types";
import {
  IsoDateTime,
  PageHeader,
  Pager,
  panelClassName,
  selectClassName,
  textInputClassName,
} from "./host-ui";

const PAGE_SIZE = 50;

function levelTone(level: string) {
  if (level === "Critical" || level === "Error") return "red" as const;
  if (level === "Warning") return "yellow" as const;
  return "slate" as const;
}

/**
 * One record. Collapsed by default and expanded on demand: a stack trace is several hundred lines and
 * the list is only useful if you can scan it.
 */
function LogRow({ entry }: { entry: InstanceLogEntry }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <li className="border-t border-border p-4">
      <div className="flex flex-wrap items-start gap-3">
        <StatusBadge status={entry.level} tone={levelTone(entry.level)} />
        <div className="min-w-0 flex-1">
          <p className="break-words text-sm">{entry.message}</p>
          <p className="mt-1 font-mono text-xs text-muted-foreground">{entry.category}</p>
          <p className="mt-1 text-xs text-muted-foreground">
            <IsoDateTime value={entry.createdAtUtc} />
            {entry.correlationId ? <> · correlation {entry.correlationId}</> : null}
          </p>
        </div>
        {entry.exception ? (
          <button
            type="button"
            onClick={() => setExpanded((current) => !current)}
            aria-expanded={expanded}
            className="rounded-lg border border-border px-2 py-1 text-xs font-medium hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            {expanded ? "Hide" : "Show"} exception
          </button>
        ) : null}
      </div>
      {expanded && entry.exception ? (
        <pre className="mt-3 max-h-96 overflow-auto rounded-lg bg-muted p-3 text-xs leading-5">
          {entry.exception}
        </pre>
      ) : null}
    </li>
  );
}

export function LogsPageClient() {
  const [level, setLevel] = useState("Warning");
  const [search, setSearch] = useState("");
  const [skip, setSkip] = useState(0);

  const input = { level: level || undefined, search: search || undefined, skip, take: PAGE_SIZE };
  const logsQuery = useQuery({ queryKey: hostKeys.logs(input), queryFn: () => listInstanceLogs(input) });

  function updateFilter(apply: () => void) {
    apply();
    setSkip(0);
  }

  return (
    <section aria-labelledby="host-logs-title" className="space-y-6">
      <PageHeader
        id="host-logs-title"
        eyebrow="Host administration"
        title="Logs"
        description="Warnings and errors captured from this server, kept for a short retention window. Full-fidelity logs live in your OpenTelemetry pipeline; this is the operator-visible slice of them."
      />

      <div className="rounded-[var(--radius)] border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
        Log messages can contain user data. Retention is deliberately short — see the Health page for
        the configured window and minimum level.
      </div>

      <div className="flex flex-wrap gap-3">
        <label className="sr-only" htmlFor="log-level">Minimum level</label>
        <select
          id="log-level"
          value={level}
          onChange={(event) => updateFilter(() => setLevel(event.target.value))}
          className={selectClassName}
        >
          {/* Each option means "this level and worse", matching the API's ladder. */}
          <option value="">Any level</option>
          <option value="Warning">Warning and above</option>
          <option value="Error">Error and above</option>
          <option value="Critical">Critical only</option>
        </select>
        <label className="sr-only" htmlFor="log-search">Search logs</label>
        <input
          id="log-search"
          type="search"
          value={search}
          onChange={(event) => updateFilter(() => setSearch(event.target.value))}
          placeholder="Message, exception or correlation id"
          className={`${textInputClassName} min-w-56 flex-1`}
        />
      </div>

      <QueryState query={logsQuery} loadingLabel="Loading logs…">
        {logsQuery.data && logsQuery.data.items.length === 0 ? (
          <EmptyState
            title="Nothing logged"
            description="No records match these filters. A quiet log at Warning and above is the healthy case."
          />
        ) : logsQuery.data ? (
          <div className={panelClassName}>
            <ul>
              {logsQuery.data.items.map((entry) => (
                <LogRow key={entry.id} entry={entry} />
              ))}
            </ul>
            <Pager skip={skip} take={PAGE_SIZE} total={logsQuery.data.total} onChange={setSkip} />
          </div>
        ) : null}
      </QueryState>
    </section>
  );
}
