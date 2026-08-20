"use client";

import { useMutation } from "@tanstack/react-query";
import { useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import { selectClassName, textInputClassName } from "@/components/admin/admin-ui";
import { addStatus, removeStatus, renameStatusScheme, updateStatus } from "@/lib/work/client";
import type { StatusCategory, StatusDefinition, StatusScheme } from "@/lib/work/types";
import { cn } from "@/lib/utils";
import { useFocusTrap } from "./useFocusTrap";

const categories: StatusCategory[] = ["NotStarted", "Active", "Done", "Closed"];
const categoryLabels: Record<StatusCategory, string> = {
  NotStarted: "Not started",
  Active: "Active",
  Done: "Done",
  Closed: "Closed",
};

function errorMessage(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback;
}

/**
 * Deleting a status always moves its tasks somewhere — the API requires `moveTasksToStatusId`, so
 * this dialog is the only way out of a remove. Exported for its own test.
 */
export function RemoveStatusDialog({
  scheme,
  status,
  onClose,
  onRemoved,
}: {
  scheme: StatusScheme;
  status: StatusDefinition;
  onClose: () => void;
  onRemoved: () => void;
}) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const others = scheme.statuses.filter((s) => s.id !== status.id);
  const [targetId, setTargetId] = useState(
    () => (others.find((s) => s.category === "NotStarted") ?? others[0])?.id ?? "",
  );

  useFocusTrap({ open: true, containerRef: dialogRef, onClose });

  const mutation = useMutation({
    mutationFn: () => removeStatus(scheme.id, status.id, targetId),
    onSuccess: () => {
      onRemoved();
      onClose();
    },
  });

  return (
    <div className="fixed inset-0 z-[60]" role="presentation">
      <button
        type="button"
        aria-label="Cancel removing this status"
        className="absolute inset-0 cursor-default bg-slate-950/50 backdrop-blur-[1px]"
        onClick={onClose}
      />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="remove-status-title"
        tabIndex={-1}
        className="absolute left-1/2 top-1/2 w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-border bg-card p-5 shadow-2xl outline-none"
      >
        <h3 id="remove-status-title" className="text-lg font-semibold">
          Remove &ldquo;{status.name}&rdquo;
        </h3>
        <form
          className="mt-4 grid gap-3"
          onSubmit={(event: FormEvent) => {
            event.preventDefault();
            if (targetId) {
              mutation.mutate();
            }
          }}
        >
          <label className="grid gap-1 text-sm">
            Move the tasks currently in <strong>{status.name}</strong> to:
            <select
              aria-label="Replacement status"
              value={targetId}
              disabled={mutation.isPending}
              onChange={(event) => setTargetId(event.currentTarget.value)}
              className={selectClassName}
            >
              <option value="">Select a status…</option>
              {others.map((other) => (
                <option key={other.id} value={other.id}>
                  {other.name}
                </option>
              ))}
            </select>
          </label>

          {mutation.isError ? (
            <p role="alert" className="text-sm text-red-600 dark:text-red-400">
              {errorMessage(mutation.error, "Could not remove this status.")}
            </p>
          ) : null}

          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" size="sm" onClick={onClose}>
              Cancel
            </Button>
            <Button
              type="submit"
              size="sm"
              disabled={targetId === "" || mutation.isPending}
              className="bg-red-600 text-white [@media(hover:hover)]:hover:bg-red-700"
            >
              Remove and move tasks
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}

/**
 * Edits one status scheme in place: rename, recolour, recategorize, reorder, add and remove statuses.
 * Shared by the workspace defaults page and a Space's override page — `canManage` only hides dead
 * controls, the API is what actually enforces permission.
 */
export function StatusSchemeEditor({
  scheme,
  canManage,
  onChanged,
}: {
  scheme: StatusScheme;
  canManage: boolean;
  onChanged?: () => void;
}) {
  const [removing, setRemoving] = useState<StatusDefinition | null>(null);
  const [newName, setNewName] = useState("");
  const [newCategory, setNewCategory] = useState<StatusCategory>("NotStarted");

  // One mutation for every edit on this card: they all return the updated scheme and all refresh the
  // same query, so a per-field mutation would just be four copies of this.
  const edit = useMutation({
    mutationFn: (call: () => Promise<unknown>) => call(),
    onSuccess: () => onChanged?.(),
  });
  const locked = !canManage || edit.isPending;

  function addNewStatus(event: FormEvent) {
    event.preventDefault();
    const name = newName.trim();
    if (name) {
      setNewName("");
      edit.mutate(() => addStatus(scheme.id, { name, category: newCategory }));
    }
  }

  return (
    <div>
      <div className="flex items-center gap-2 p-4">
        <label className="sr-only" htmlFor={`scheme-name-${scheme.id}`}>
          Workflow name
        </label>
        <input
          id={`scheme-name-${scheme.id}`}
          // Uncontrolled + re-keyed on the server value: commit on blur so a rename is one PATCH, not
          // one per keystroke, while a server-side change still resets the field.
          key={scheme.name}
          defaultValue={scheme.name}
          disabled={locked}
          onBlur={(event) => {
            const name = event.currentTarget.value.trim();
            if (name && name !== scheme.name) {
              edit.mutate(() => renameStatusScheme(scheme.id, name));
            }
          }}
          className={cn(textInputClassName, "flex-1 font-semibold")}
        />
      </div>

      <div className="divide-y divide-border border-y border-border">
        {scheme.statuses.map((status, index) => (
          <div key={status.id} className="flex flex-wrap items-center gap-2 p-3">
            <input
              type="color"
              key={`${status.id}-${status.color}`}
              defaultValue={status.color}
              disabled={locked}
              aria-label={`Colour for ${status.name}`}
              onBlur={(event) => {
                const color = event.currentTarget.value;
                if (color !== status.color) {
                  edit.mutate(() => updateStatus(scheme.id, status.id, { color }));
                }
              }}
              className="size-9 shrink-0 rounded-lg border border-border bg-background disabled:opacity-50"
            />
            <input
              key={`${status.id}-${status.name}`}
              defaultValue={status.name}
              disabled={locked}
              aria-label={`Name of ${status.name}`}
              onBlur={(event) => {
                const name = event.currentTarget.value.trim();
                if (name && name !== status.name) {
                  edit.mutate(() => updateStatus(scheme.id, status.id, { name }));
                }
              }}
              className={cn(textInputClassName, "min-w-40 flex-1")}
            />
            <select
              value={status.category}
              disabled={locked}
              aria-label={`Category of ${status.name}`}
              // Read the value HERE, not inside the mutation closure: currentTarget is nulled once
              // the handler returns, so deferring the read threw before the request was ever sent —
              // and react-query swallowed it into mutation state, so the category silently never saved.
              onChange={(event) => {
                const category = event.currentTarget.value as StatusCategory;
                edit.mutate(() => updateStatus(scheme.id, status.id, { category }));
              }}
              className={selectClassName}
            >
              {categories.map((category) => (
                <option key={category} value={category}>
                  {categoryLabels[category]}
                </option>
              ))}
            </select>
            <Button
              variant="ghost"
              size="sm"
              aria-label={`Move ${status.name} up`}
              disabled={locked || index === 0}
              onClick={() => edit.mutate(() => updateStatus(scheme.id, status.id, { index: index - 1 }))}
            >
              ↑
            </Button>
            <Button
              variant="ghost"
              size="sm"
              aria-label={`Move ${status.name} down`}
              disabled={locked || index === scheme.statuses.length - 1}
              onClick={() => edit.mutate(() => updateStatus(scheme.id, status.id, { index: index + 1 }))}
            >
              ↓
            </Button>
            <Button
              variant="ghost"
              size="sm"
              aria-label={`Remove ${status.name}`}
              disabled={locked}
              className="text-red-600 dark:text-red-400"
              onClick={() => setRemoving(status)}
            >
              Remove
            </Button>
          </div>
        ))}
      </div>

      {canManage ? (
        <form onSubmit={addNewStatus} className="flex flex-wrap items-center gap-2 p-3">
          <input
            value={newName}
            disabled={edit.isPending}
            aria-label="New status name"
            placeholder="Add a status…"
            onChange={(event) => setNewName(event.currentTarget.value)}
            className={cn(textInputClassName, "min-w-40 flex-1")}
          />
          <select
            value={newCategory}
            disabled={edit.isPending}
            aria-label="New status category"
            onChange={(event) => setNewCategory(event.currentTarget.value as StatusCategory)}
            className={selectClassName}
          >
            {categories.map((category) => (
              <option key={category} value={category}>
                {categoryLabels[category]}
              </option>
            ))}
          </select>
          <Button type="submit" size="sm" variant="secondary" disabled={edit.isPending || newName.trim() === ""}>
            Add status
          </Button>
        </form>
      ) : null}

      {edit.isError ? (
        <p role="alert" className="px-4 pb-4 text-sm text-red-600 dark:text-red-400">
          {errorMessage(edit.error, "Could not save that change.")}
        </p>
      ) : null}

      {removing ? (
        <RemoveStatusDialog
          scheme={scheme}
          status={removing}
          onClose={() => setRemoving(null)}
          onRemoved={() => onChanged?.()}
        />
      ) : null}
    </div>
  );
}
