"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { useAppContext } from "@/lib/app-context/AppContext";
import {
  approveTimesheet,
  getPolicy,
  getTimesheet,
  listTimeTags,
  rejectTimesheet,
  reopenTimesheet,
  submitTimesheet,
} from "@/lib/time/client";
import { formatDuration, formatDecimalHours } from "@/lib/time/format";
import { timeKeys } from "@/lib/time/queries";
import type { TimeEntry, TimeApprovalStatus } from "@/lib/time/types";
import { cn } from "@/lib/utils";

const statusClassName: Record<TimeApprovalStatus, string> = {
  Draft: "bg-muted text-muted-foreground",
  Submitted: "bg-blue-100 text-blue-700 dark:bg-blue-950 dark:text-blue-300",
  Approved: "bg-green-100 text-green-700 dark:bg-green-950 dark:text-green-300",
  Rejected: "bg-red-100 text-red-700 dark:bg-red-950 dark:text-red-300",
  Locked: "bg-slate-200 text-slate-700 dark:bg-slate-800 dark:text-slate-300",
};

function startOfWeek(date: Date, weekStartsOn: number) {
  const next = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
  const diff = (next.getUTCDay() - weekStartsOn + 7) % 7;
  next.setUTCDate(next.getUTCDate() - diff);
  return next;
}

function addDays(date: Date, days: number) {
  const next = new Date(date);
  next.setUTCDate(next.getUTCDate() + days);
  return next;
}

function dayKey(date: Date | string) {
  return new Date(date).toISOString().slice(0, 10);
}

function formatDay(date: Date) {
  return new Intl.DateTimeFormat("en", { weekday: "short", month: "short", day: "numeric" }).format(date);
}

function formatEntryTime(entry: TimeEntry) {
  const formatter = new Intl.DateTimeFormat("en", { hour: "numeric", minute: "2-digit" });
  return `${formatter.format(new Date(entry.startedAtUtc))}${
    entry.endedAtUtc ? `–${formatter.format(new Date(entry.endedAtUtc))}` : ""
  }`;
}

export default function TimesheetsPage() {
  const queryClient = useQueryClient();
  const { currentWorkspace } = useAppContext();
  const isAdmin = currentWorkspace?.role === "Admin" || currentWorkspace?.role === "Owner";
  const policyQuery = useQuery({ queryKey: timeKeys.policy(), queryFn: getPolicy });
  const weekStartsOn = policyQuery.data?.weekStartsOn ?? 1;
  const [weekStart, setWeekStart] = useState(() => startOfWeek(new Date(), weekStartsOn));
  const [rejectComment, setRejectComment] = useState("");
  const [tagId, setTagId] = useState<string | undefined>(undefined);
  const weekStartIso = weekStart.toISOString();
  const tagsQuery = useQuery({ queryKey: timeKeys.tags(), queryFn: listTimeTags });
  const timesheetParams = { weekStart: weekStartIso, tagId };
  const timesheetQuery = useQuery({
    queryKey: timeKeys.timesheet(timesheetParams),
    queryFn: () => getTimesheet(timesheetParams),
  });
  const timesheet = timesheetQuery.data;
  const days = useMemo(() => Array.from({ length: 7 }, (_, index) => addDays(weekStart, index)), [weekStart]);
  const entriesByDay = useMemo(() => {
    const grouped = new Map<string, TimeEntry[]>();
    days.forEach((day) => grouped.set(dayKey(day), []));
    timesheet?.entries.forEach((entry) => grouped.get(dayKey(entry.startedAtUtc))?.push(entry));
    return grouped;
  }, [days, timesheet?.entries]);
  const invalidateTimesheets = () => queryClient.invalidateQueries({ queryKey: timeKeys.all });
  const submitMutation = useMutation({
    mutationFn: submitTimesheet,
    onSuccess: () => {
      void invalidateTimesheets();
    },
  });
  const approveMutation = useMutation({
    mutationFn: approveTimesheet,
    onSuccess: () => {
      void invalidateTimesheets();
    },
  });
  const rejectMutation = useMutation({
    mutationFn: ({ id, comment }: { id: string; comment: string }) => rejectTimesheet(id, comment),
    onSuccess: () => {
      setRejectComment("");
      void invalidateTimesheets();
    },
  });
  const reopenMutation = useMutation({
    mutationFn: reopenTimesheet,
    onSuccess: () => {
      void invalidateTimesheets();
    },
  });
  const isReadOnly = timesheet?.status === "Approved" || timesheet?.status === "Locked";
  const canSubmit = Boolean(timesheet && (timesheet.status === "Draft" || timesheet.status === "Rejected"));
  const canApprove = timesheet?.status === "Submitted";
  const canReopen = isAdmin && (timesheet?.status === "Approved" || timesheet?.status === "Locked");

  function moveWeek(offset: number) {
    setWeekStart((current) => addDays(current, offset * 7));
  }

  return (
    <section aria-labelledby="timesheets-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Time tracking</p>
          <h1 id="timesheets-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Timesheets
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Review weekly hours, billable time, submission state, and approval actions.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <label className="grid gap-1 text-xs font-medium">
            <span className="sr-only">Filter by tag</span>
            <select
              value={tagId ?? ""}
              className="h-9 rounded-lg border border-border bg-background px-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onChange={(event) => setTagId(event.target.value || undefined)}
            >
              <option value="">All tags</option>
              {(tagsQuery.data ?? []).map((tag) => (
                <option key={tag.id} value={tag.id}>
                  {tag.name}
                </option>
              ))}
            </select>
          </label>
          <Button type="button" variant="outline" size="sm" onClick={() => moveWeek(-1)}>
            Previous week
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={() => setWeekStart(startOfWeek(new Date(), weekStartsOn))}>
            This week
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={() => moveWeek(1)}>
            Next week
          </Button>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-4">
        <article className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Week of</p>
          <p className="mt-2 text-xl font-semibold">
            {new Intl.DateTimeFormat("en", { month: "long", day: "numeric", year: "numeric" }).format(weekStart)}
          </p>
        </article>
        <article className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Total hours</p>
          <p className="mt-2 text-xl font-semibold">{formatDuration(timesheet?.totalSeconds ?? 0)}</p>
        </article>
        <article className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Billable</p>
          <p className="mt-2 text-xl font-semibold">{formatDuration(timesheet?.billableSeconds ?? 0)}</p>
        </article>
        <article className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Status</p>
          <div className="mt-2 flex items-center gap-2">
            <span
              className={cn(
                "rounded-full px-2 py-1 text-xs font-semibold",
                statusClassName[timesheet?.status ?? "Draft"],
              )}
            >
              {timesheet?.status ?? "Draft"}
            </span>
            {isReadOnly ? <span className="text-xs text-muted-foreground">Rows read-only</span> : null}
          </div>
        </article>
      </div>

      <section className="rounded-[var(--radius)] border border-border bg-card shadow-sm" aria-label="Weekly timesheet grid">
        <div className="flex flex-col gap-3 border-b border-border p-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="text-sm font-semibold">Weekly grid</h2>
            <p className="text-xs text-muted-foreground">
              {formatDecimalHours(timesheet?.totalSeconds ?? 0)} total · {timesheet?.entries.length ?? 0} entries
            </p>
          </div>
          <Button
            type="button"
            size="sm"
            disabled={!canSubmit || submitMutation.isPending}
            onClick={() => submitMutation.mutate(weekStartIso)}
          >
            Submit timesheet
          </Button>
        </div>
        {timesheetQuery.isLoading ? (
          <p className="p-4 text-sm text-muted-foreground">Loading timesheet…</p>
        ) : (
          <div className="grid gap-px bg-border md:grid-cols-7">
            {days.map((day) => {
              const dayEntries = entriesByDay.get(dayKey(day)) ?? [];
              const dayTotal = dayEntries.reduce((total, entry) => total + entry.durationSeconds, 0);

              return (
                <section key={day.toISOString()} className="min-h-56 bg-card p-3" aria-labelledby={`day-${dayKey(day)}`}>
                  <div className="flex items-center justify-between gap-2">
                    <h3 id={`day-${dayKey(day)}`} className="text-sm font-semibold">
                      {formatDay(day)}
                    </h3>
                    <span className="text-xs text-muted-foreground">{formatDuration(dayTotal)}</span>
                  </div>
                  <div className="mt-3 space-y-2">
                    {dayEntries.length === 0 ? (
                      <p className="rounded-lg border border-dashed border-border p-3 text-xs text-muted-foreground">
                        No time logged.
                      </p>
                    ) : (
                      dayEntries.map((entry) => {
                        const entryReadOnly = entry.approvalStatus === "Approved" || entry.approvalStatus === "Locked";

                        return (
                          <article
                            key={entry.id}
                            className={cn(
                              "rounded-lg border border-border bg-background p-3 text-xs",
                              entryReadOnly && "opacity-80",
                            )}
                          >
                            <p className="font-semibold">{entry.description || "Untitled entry"}</p>
                            <p className="mt-1 text-muted-foreground">
                              {formatEntryTime(entry)} · {formatDuration(entry.durationSeconds)}
                            </p>
                            <div className="mt-2 flex flex-wrap gap-1.5">
                              <span className={cn("rounded-full px-2 py-0.5 font-medium", statusClassName[entry.approvalStatus])}>
                                {entry.approvalStatus}
                              </span>
                              {entry.isBillable ? (
                                <span className="rounded-full border border-primary/40 px-2 py-0.5 font-medium text-primary">
                                  Billable
                                </span>
                              ) : null}
                              {entry.tags.map((tag) => (
                                <span key={tag.id} className="rounded-full bg-muted px-2 py-0.5 font-medium text-muted-foreground">
                                  {tag.name}
                                </span>
                              ))}
                            </div>
                          </article>
                        );
                      })
                    )}
                  </div>
                </section>
              );
            })}
          </div>
        )}
      </section>

      <section className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm" aria-labelledby="approval-title">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <h2 id="approval-title" className="text-sm font-semibold">Approval</h2>
            <p className="mt-1 text-xs text-muted-foreground">
              Approver controls become available once the timesheet is submitted.
            </p>
          </div>
          <div className="flex flex-col gap-2 sm:min-w-96">
            <label className="grid gap-1 text-xs font-medium">
              Rejection comment
              <textarea
                rows={3}
                value={rejectComment}
                className="resize-y rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50"
                disabled={!canApprove}
                onChange={(event) => setRejectComment(event.target.value)}
              />
            </label>
            <div className="flex flex-wrap justify-end gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={!canApprove || !rejectComment.trim() || rejectMutation.isPending || !timesheet}
                onClick={() => timesheet && rejectMutation.mutate({ id: timesheet.id, comment: rejectComment.trim() })}
              >
                Reject
              </Button>
              <Button
                type="button"
                size="sm"
                disabled={!canApprove || approveMutation.isPending || !timesheet}
                onClick={() => timesheet && approveMutation.mutate(timesheet.id)}
              >
                Approve
              </Button>
              {canReopen ? (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={reopenMutation.isPending || !timesheet}
                  onClick={() => timesheet && reopenMutation.mutate(timesheet.id)}
                >
                  Reopen
                </Button>
              ) : null}
            </div>
          </div>
        </div>
      </section>
    </section>
  );
}
