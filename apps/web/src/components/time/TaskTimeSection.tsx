"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FormEvent, useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { createEntry, deleteEntry, listEntries, updateEntry } from "@/lib/time/client";
import {
  formatDuration,
  formatDecimalHours,
  fromLocalDateTimeInputValue,
  toLocalDateTimeInputValue,
} from "@/lib/time/format";
import { timeKeys } from "@/lib/time/queries";
import type { TimeEntry, TimeTag, UpdateTimeEntryPatch } from "@/lib/time/types";
import { cn } from "@/lib/utils";
import { DurationInput } from "./DurationInput";
import { TagInput } from "./TagInput";
import { TaskTimerButton } from "./TaskTimerButton";

const statusClassName: Record<TimeEntry["approvalStatus"], string> = {
  Draft: "bg-muted text-muted-foreground",
  Submitted: "bg-blue-100 text-blue-700 dark:bg-blue-950 dark:text-blue-300",
  Approved: "bg-green-100 text-green-700 dark:bg-green-950 dark:text-green-300",
  Rejected: "bg-red-100 text-red-700 dark:bg-red-950 dark:text-red-300",
  Locked: "bg-slate-200 text-slate-700 dark:bg-slate-800 dark:text-slate-300",
};

function formatEntryTime(entry: TimeEntry) {
  const formatter = new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });

  return `${formatter.format(new Date(entry.startedAtUtc))}${
    entry.endedAtUtc ? `–${formatter.format(new Date(entry.endedAtUtc))}` : ""
  }`;
}

function defaultStartInput() {
  const date = new Date();
  date.setMinutes(0, 0, 0);
  return toLocalDateTimeInputValue(date);
}

const editFieldClassName =
  "rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

/**
 * Inline edit for a finished entry. The API's PATCH derives duration from start/end, so the editor
 * exposes the two timestamps rather than a duration box.
 */
function EntryEditor({
  entry,
  fallbackDescription,
  isSaving,
  onCancel,
  onSave,
}: {
  entry: TimeEntry;
  fallbackDescription: string;
  isSaving: boolean;
  onCancel: () => void;
  onSave: (patch: {
    startedAtUtc: string;
    endedAtUtc: string;
    description: string;
    isBillable: boolean;
    tagIds: string[];
  }) => void;
}) {
  const [start, setStart] = useState(() => toLocalDateTimeInputValue(new Date(entry.startedAtUtc)));
  const [end, setEnd] = useState(() =>
    entry.endedAtUtc ? toLocalDateTimeInputValue(new Date(entry.endedAtUtc)) : "",
  );
  const [description, setDescription] = useState(entry.description ?? "");
  const [isBillable, setIsBillable] = useState(entry.isBillable);
  const [tags, setTags] = useState<TimeTag[]>(entry.tags);
  const isRangeValid = Boolean(start) && Boolean(end) && new Date(end) >= new Date(start);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!isRangeValid) {
      return;
    }

    onSave({
      startedAtUtc: fromLocalDateTimeInputValue(start),
      endedAtUtc: fromLocalDateTimeInputValue(end),
      description: description.trim() || fallbackDescription,
      isBillable,
      tagIds: tags.map((t) => t.id),
    });
  }

  return (
    <form className="grid gap-3" onSubmit={handleSubmit}>
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="grid gap-1 text-xs font-medium">
          Start
          <input
            type="datetime-local"
            value={start}
            className={editFieldClassName}
            onChange={(event) => setStart(event.target.value)}
          />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          End
          <input
            type="datetime-local"
            value={end}
            className={editFieldClassName}
            onChange={(event) => setEnd(event.target.value)}
          />
        </label>
      </div>
      <label className="grid gap-1 text-xs font-medium">
        Description
        <input
          type="text"
          value={description}
          className={editFieldClassName}
          onChange={(event) => setDescription(event.target.value)}
        />
      </label>
      <label className="inline-flex w-fit items-center gap-2 rounded-lg border border-border bg-background px-3 py-2 text-sm">
        <input
          type="checkbox"
          checked={isBillable}
          className="size-4 accent-[var(--primary)]"
          onChange={(event) => setIsBillable(event.target.checked)}
        />
        Billable
      </label>
      <TagInput selected={tags} onChange={setTags} />
      <div className="flex flex-wrap items-center gap-2">
        <Button type="submit" size="sm" disabled={isSaving || !isRangeValid}>
          Save entry
        </Button>
        <Button type="button" size="sm" variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
        {!isRangeValid ? (
          <span className="text-xs text-muted-foreground">End must be on or after start.</span>
        ) : null}
      </div>
    </form>
  );
}

export function TaskTimeSection({
  taskId,
  taskTitle,
}: {
  taskId: string;
  taskTitle: string;
}) {
  const queryClient = useQueryClient();
  const [editingEntryId, setEditingEntryId] = useState<string | null>(null);
  const [startInput, setStartInput] = useState(defaultStartInput);
  const [endInput, setEndInput] = useState("");
  const [duration, setDuration] = useState({ text: "1h", seconds: 3600 as number | null });
  const [description, setDescription] = useState("");
  const [isBillable, setIsBillable] = useState(true);
  const [newEntryTags, setNewEntryTags] = useState<TimeTag[]>([]);
  const range = useMemo(() => {
    const from = new Date();
    from.setDate(from.getDate() - 14);
    from.setHours(0, 0, 0, 0);
    const to = new Date();
    to.setDate(to.getDate() + 7);
    to.setHours(23, 59, 59, 999);

    return { from: from.toISOString(), to: to.toISOString() };
  }, []);
  const entriesQuery = useQuery({
    queryKey: timeKeys.entries(range),
    queryFn: () => listEntries(range),
    select: (entries) => entries.filter((entry) => entry.taskId === taskId),
  });
  const entries = entriesQuery.data ?? [];
  const totalSeconds = entries.reduce((total, entry) => total + entry.durationSeconds, 0);
  const billableSeconds = entries.reduce(
    (total, entry) => total + (entry.isBillable ? entry.durationSeconds : 0),
    0,
  );
  const invalidateTime = () => queryClient.invalidateQueries({ queryKey: timeKeys.all });
  const createMutation = useMutation({
    mutationFn: createEntry,
    onSuccess: () => {
      setDescription("");
      setDuration({ text: "1h", seconds: 3600 });
      setEndInput("");
      setNewEntryTags([]);
      void invalidateTime();
    },
  });
  const deleteMutation = useMutation({
    mutationFn: deleteEntry,
    onSuccess: () => {
      void invalidateTime();
    },
  });
  const updateMutation = useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: UpdateTimeEntryPatch }) => updateEntry(id, patch),
    onSuccess: () => {
      setEditingEntryId(null);
      void invalidateTime();
    },
  });
  const mutationError = createMutation.error ?? updateMutation.error ?? deleteMutation.error;

  const durationSeconds = duration.seconds && duration.seconds > 0 ? duration.seconds : undefined;

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!durationSeconds && !endInput) {
      return;
    }

    const startedAtUtc = fromLocalDateTimeInputValue(startInput);
    const endedAtUtc = durationSeconds
      ? new Date(new Date(startedAtUtc).getTime() + durationSeconds * 1000).toISOString()
      : fromLocalDateTimeInputValue(endInput);

    createMutation.mutate({
      taskId,
      startedAtUtc,
      endedAtUtc,
      durationSeconds,
      description: description.trim() || taskTitle,
      isBillable,
      tagIds: newEntryTags.map((t) => t.id),
    });
  }

  return (
    <section aria-labelledby="detail-time" className="space-y-4 rounded-xl border border-border p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h3 id="detail-time" className="text-sm font-semibold">
            Time
          </h3>
          <p className="mt-1 text-xs text-muted-foreground">
            {formatDecimalHours(totalSeconds)} logged · {formatDecimalHours(billableSeconds)} billable
          </p>
        </div>
        <TaskTimerButton taskId={taskId} taskTitle={taskTitle} />
      </div>

      {mutationError ? (
        <p
          role="alert"
          className="rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          This time change could not be saved: {(mutationError as Error).message}
        </p>
      ) : null}

      <form className="grid gap-3 rounded-lg bg-muted/50 p-3" onSubmit={handleSubmit}>
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="grid gap-1 text-xs font-medium">
            Start
            <input
              type="datetime-local"
              value={startInput}
              className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onChange={(event) => setStartInput(event.target.value)}
            />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            End
            <input
              type="datetime-local"
              value={endInput}
              className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onChange={(event) => setEndInput(event.target.value)}
            />
          </label>
        </div>
        <div className="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-start">
          <DurationInput
            value={duration.text}
            onChange={(text, seconds) => setDuration({ text, seconds })}
          />
          <label className="inline-flex h-fit items-center gap-2 rounded-lg border border-border bg-background px-3 py-2 text-sm sm:mt-[1.375rem]">
            <input
              type="checkbox"
              checked={isBillable}
              className="size-4 accent-[var(--primary)]"
              onChange={(event) => setIsBillable(event.target.checked)}
            />
            Billable
          </label>
        </div>
        <label className="grid gap-1 text-xs font-medium">
          Description
          <input
            type="text"
            value={description}
            placeholder="What did you work on?"
            className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            onChange={(event) => setDescription(event.target.value)}
          />
        </label>
        <TagInput selected={newEntryTags} onChange={setNewEntryTags} />
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-xs text-muted-foreground">Duration overrides end time when provided.</p>
          <Button
            type="submit"
            size="sm"
            disabled={createMutation.isPending || (!durationSeconds && !endInput)}
          >
            Add manual entry
          </Button>
        </div>
      </form>

      <div className="space-y-2">
        {entriesQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">Loading time entries…</p>
        ) : entries.length === 0 ? (
          <p className="rounded-lg border border-dashed border-border p-3 text-sm text-muted-foreground">
            No entries for this task yet.
          </p>
        ) : (
          <ul className="space-y-2">
            {entries.map((entry) => {
              const readOnly = entry.approvalStatus === "Approved" || entry.approvalStatus === "Locked";

              return (
                <li
                  key={entry.id}
                  className="rounded-lg border border-border bg-background p-3 text-sm"
                >
                  {editingEntryId === entry.id ? (
                    <EntryEditor
                      entry={entry}
                      fallbackDescription={taskTitle}
                      isSaving={updateMutation.isPending}
                      onCancel={() => setEditingEntryId(null)}
                      onSave={(patch) => updateMutation.mutate({ id: entry.id, patch })}
                    />
                  ) : (
                    <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                      <div>
                        <p className="font-medium">{entry.description || taskTitle}</p>
                        <p className="mt-1 text-xs text-muted-foreground">
                          {formatEntryTime(entry)} · {formatDuration(entry.durationSeconds)}
                          {entry.endedAtUtc ? "" : " · running"}
                        </p>
                      </div>
                      <div className="flex flex-wrap items-center gap-2">
                        <span
                          className={cn(
                            "rounded-full px-2 py-0.5 text-xs font-medium",
                            statusClassName[entry.approvalStatus],
                          )}
                        >
                          {entry.approvalStatus}
                        </span>
                        {entry.isBillable ? (
                          <span className="rounded-full border border-primary/40 px-2 py-0.5 text-xs font-medium text-primary">
                            Billable
                          </span>
                        ) : null}
                        {entry.tags.map((tag) => (
                          <span key={tag.id} className="rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                            {tag.name}
                          </span>
                        ))}
                        {!readOnly && entry.endedAtUtc ? (
                          <>
                            <Button
                              type="button"
                              size="sm"
                              variant="ghost"
                              className="h-7 px-2 text-xs"
                              onClick={() => setEditingEntryId(entry.id)}
                            >
                              Edit
                            </Button>
                            <Button
                              type="button"
                              size="sm"
                              variant="ghost"
                              className="h-7 px-2 text-xs text-red-600 hover:text-red-700 dark:text-red-400"
                              disabled={deleteMutation.isPending}
                              onClick={() => deleteMutation.mutate(entry.id)}
                            >
                              Delete
                            </Button>
                          </>
                        ) : null}
                      </div>
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </section>
  );
}
