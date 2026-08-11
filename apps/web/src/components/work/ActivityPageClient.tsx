"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { getWorkspaceActivity } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import { useMemberDirectory, useMembers } from "@/lib/members";

const ACTIVITY_TYPE_LABELS: Record<string, string> = {
  created: "created",
  created_from_recurrence: "created (recurring)",
  merged_from: "merged from another task",
  list_added: "added to a list",
  list_removed: "removed from a list",
  team_assigned: "assigned to a team",
  status_changed: "changed status",
  moved: "moved",
  completed: "completed",
  reopened: "reopened",
  assigned: "assigned",
  priority_changed: "changed priority",
  dates_changed: "changed dates",
  task_type_changed: "changed task type",
  custom_id_changed: "changed custom id",
  custom_field_changed: "changed a custom field",
  dependency_added: "added a dependency",
  dependency_removed: "removed a dependency",
};

function formatTime(value: string) {
  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(value));
}

/**
 * Workspace-wide Activity view. GET /api/v1/activity is permission-filtered server-side
 * (WorkspaceActivityService) -- a Member never sees an event for a task/list/space they can't
 * otherwise read, same ACL check every task listing endpoint already applies. This page just renders
 * what the API already filtered; there is no additional client-side privacy logic to get wrong.
 */
export function ActivityPageClient() {
  const [actorUserId, setActorUserId] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const directory = useMemberDirectory();
  const membersQuery = useMembers();

  const query = useMemo(
    () => ({
      take: 100,
      actorUserId: actorUserId || undefined,
      from: fromDate ? `${fromDate}T00:00:00Z` : undefined,
      to: toDate ? `${toDate}T23:59:59Z` : undefined,
    }),
    [actorUserId, fromDate, toDate],
  );

  const activityQuery = useQuery({
    queryKey: workKeys.activity(query),
    queryFn: () => getWorkspaceActivity(query),
  });
  const events = activityQuery.data ?? [];

  return (
    <section className="space-y-6">
      <div>
        <p className="text-sm font-medium uppercase tracking-wide text-muted-foreground">Views</p>
        <h1 className="text-3xl font-semibold tracking-tight">Activity</h1>
        <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
          Recent task activity across the workspace, filtered to what you can see.
        </p>
      </div>

      <div className="flex flex-wrap items-end gap-3 rounded-2xl border border-border bg-card p-4">
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-xs font-medium text-muted-foreground">Actor</span>
          <select
            className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
            value={actorUserId}
            onChange={(event) => setActorUserId(event.target.value)}
          >
            <option value="">Everyone</option>
            {(membersQuery.data ?? []).map((member) => (
              <option key={member.userId} value={member.userId}>
                {member.displayName || member.email || member.userId}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-xs font-medium text-muted-foreground">From</span>
          <input
            type="date"
            className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
            value={fromDate}
            onChange={(event) => setFromDate(event.target.value)}
          />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-xs font-medium text-muted-foreground">To</span>
          <input
            type="date"
            className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
            value={toDate}
            onChange={(event) => setToDate(event.target.value)}
          />
        </label>
        <p className="ml-auto text-sm text-muted-foreground">{events.length} events</p>
      </div>

      {activityQuery.isLoading ? (
        <div className="rounded-2xl border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading activity…
        </div>
      ) : events.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-border bg-card p-8 text-center text-sm text-muted-foreground">
          No activity matches these filters.
        </div>
      ) : (
        <ol className="space-y-2">
          {events.map((event) => (
            <li
              key={event.id}
              className="flex flex-wrap items-center gap-2 rounded-xl border border-border bg-card px-4 py-3 text-sm"
            >
              <span className="font-medium">
                {event.actorUserId ? directory.getLabel(event.actorUserId) : "Someone"}
              </span>
              <span className="text-muted-foreground">
                {ACTIVITY_TYPE_LABELS[event.type] ?? event.type}
              </span>
              <span className="font-medium">{event.taskTitle}</span>
              <time className="ml-auto text-xs text-muted-foreground" dateTime={event.createdAtUtc}>
                {formatTime(event.createdAtUtc)}
              </time>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
