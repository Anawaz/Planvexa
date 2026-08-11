"use client";

import { useQuery } from "@tanstack/react-query";
import type { FormEvent } from "react";
import { useState } from "react";
import { buttonStyles } from "@/components/ui/Button";
import { MemberSelect } from "@/components/people/MemberSelect";
import { auditExportHref, searchAudit } from "@/lib/admin/client";
import { adminKeys } from "@/lib/admin/queries";
import type { AuditSearchInput } from "@/lib/admin/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useMemberDirectory } from "@/lib/members";
import { cn } from "@/lib/utils";
import {
  IsoDateTime,
  PageHeader,
  panelClassName,
  tableHeaderClassName,
  textInputClassName,
  toUtcDateEnd,
  toUtcDateStart,
} from "./admin-ui";

type FilterDraft = {
  action: string;
  entityType: string;
  actorUserId: string;
  from: string;
  to: string;
};

const emptyFilters: FilterDraft = {
  action: "",
  entityType: "",
  actorUserId: "",
  from: "",
  to: "",
};

function buildAuditSearchInput(filters: FilterDraft): AuditSearchInput {
  const input: AuditSearchInput = {};

  if (filters.action.trim()) {
    input.action = filters.action.trim();
  }
  if (filters.entityType.trim()) {
    input.entityType = filters.entityType.trim();
  }
  if (filters.actorUserId.trim()) {
    input.actorUserId = filters.actorUserId.trim();
  }
  if (filters.from) {
    input.from = toUtcDateStart(filters.from);
  }
  if (filters.to) {
    input.to = toUtcDateEnd(filters.to);
  }

  return input;
}

export function AuditPageClient() {
  const { workspaceId = "" } = useAppContext();
  const directory = useMemberDirectory();
  const [filters, setFilters] = useState<FilterDraft>(emptyFilters);
  const [appliedFilters, setAppliedFilters] = useState<AuditSearchInput>({});
  const auditQuery = useQuery({
    queryKey: adminKeys.audit(workspaceId, appliedFilters),
    queryFn: () => searchAudit(appliedFilters),
  });
  const entries = auditQuery.data ?? [];
  const exportHref = auditExportHref(appliedFilters);

  function submitFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setAppliedFilters(buildAuditSearchInput(filters));
  }

  function resetFilters() {
    setFilters(emptyFilters);
    setAppliedFilters({});
  }

  return (
    <section aria-labelledby="audit-title" className="space-y-6">
      <PageHeader
        id="audit-title"
        eyebrow="Governance"
        title="Audit log"
        description="Search workspace audit events by actor, action, entity, and date range."
      />

      <form onSubmit={submitFilters} className={cn(panelClassName, "grid gap-4 p-5 xl:grid-cols-6")} aria-label="Audit filters">
        <label htmlFor="audit-action" className="grid gap-2 text-sm font-medium">
          Action
          <input
            id="audit-action"
            value={filters.action}
            onChange={(event) => setFilters((current) => ({ ...current, action: event.target.value }))}
            className={textInputClassName}
            placeholder="billing"
          />
        </label>
        <label htmlFor="audit-entity-type" className="grid gap-2 text-sm font-medium">
          Entity type
          <input
            id="audit-entity-type"
            value={filters.entityType}
            onChange={(event) => setFilters((current) => ({ ...current, entityType: event.target.value }))}
            className={textInputClassName}
            placeholder="Subscription"
          />
        </label>
        <label htmlFor="audit-actor" className="grid gap-2 text-sm font-medium">
          Actor
          <MemberSelect
            id="audit-actor"
            value={filters.actorUserId}
            onChange={(userId) => setFilters((current) => ({ ...current, actorUserId: userId }))}
            includeAny
            anyLabel="Anyone"
            className={textInputClassName}
          />
        </label>
        <label htmlFor="audit-from" className="grid gap-2 text-sm font-medium">
          From
          <input
            id="audit-from"
            type="date"
            value={filters.from}
            onChange={(event) => setFilters((current) => ({ ...current, from: event.target.value }))}
            className={textInputClassName}
          />
        </label>
        <label htmlFor="audit-to" className="grid gap-2 text-sm font-medium">
          To
          <input
            id="audit-to"
            type="date"
            value={filters.to}
            onChange={(event) => setFilters((current) => ({ ...current, to: event.target.value }))}
            className={textInputClassName}
          />
        </label>
        <div className="flex flex-wrap items-end gap-2">
          <button type="submit" className={buttonStyles({ size: "sm" })}>
            Apply
          </button>
          <button type="button" className={buttonStyles({ variant: "outline", size: "sm" })} onClick={resetFilters}>
            Reset
          </button>
        </div>
      </form>

      <section className={cn(panelClassName, "overflow-hidden")} aria-labelledby="audit-results-title">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border p-5">
          <div>
            <h2 id="audit-results-title" className="text-lg font-semibold">
              Results
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">
              {auditQuery.isLoading ? "Loading audit entries…" : `${entries.length} matching events`}
            </p>
          </div>
          <a href={exportHref} className={buttonStyles({ variant: "secondary", size: "sm" })}>
            Export CSV
          </a>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <caption className="sr-only">Audit log results.</caption>
            <thead className={tableHeaderClassName}>
              <tr>
                <th className="px-4 py-3 font-semibold">Time</th>
                <th className="px-4 py-3 font-semibold">Actor</th>
                <th className="px-4 py-3 font-semibold">Action</th>
                <th className="px-4 py-3 font-semibold">Entity</th>
                <th className="px-4 py-3 font-semibold">IP address</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {entries.map((entry) => (
                <tr key={entry.id}>
                  <td className="px-4 py-3 text-muted-foreground">
                    <IsoDateTime value={entry.createdAtUtc} />
                  </td>
                  <td className="px-4 py-3">
                    {entry.actorUserId ? directory.getLabel(entry.actorUserId) : "System"}
                  </td>
                  <td className="px-4 py-3 font-medium">{entry.action}</td>
                  <td className="px-4 py-3">
                    <span className="font-medium">{entry.entityType}</span>
                    <span className="ml-2 text-muted-foreground">{entry.entityId ?? "—"}</span>
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">{entry.ipAddress ?? "—"}</td>
                </tr>
              ))}
              {!auditQuery.isLoading && entries.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-4 py-6 text-center text-sm text-muted-foreground">
                    No audit entries match these filters.
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </section>
    </section>
  );
}
