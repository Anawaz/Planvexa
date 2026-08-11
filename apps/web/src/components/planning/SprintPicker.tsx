"use client";

import { useQuery } from "@tanstack/react-query";
import { listSprints } from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";

type SprintPickerProps = {
  id?: string;
  value: string;
  onChange: (sprintId: string) => void;
  disabled?: boolean;
};

/**
 * Dropdown over the workspace's sprints, keyed by id but always chosen by name — the Burndown
 * widget's sprintId config must never ask the user to know or paste a raw id (see ResourcePicker's
 * doc comment / AGENTS.md's "no raw-UUID entry" rule). A native <select> over `listSprints` is enough:
 * sprints are a small, already-fetched-elsewhere list, unlike the global search ResourcePicker wraps.
 */
export function SprintPicker({ id, value, onChange, disabled }: SprintPickerProps) {
  const { data: sprints, isLoading } = useQuery({
    queryKey: planningKeys.sprints(),
    queryFn: listSprints,
  });

  return (
    <select
      id={id}
      value={value}
      disabled={disabled || isLoading}
      onChange={(event) => onChange(event.target.value)}
      className="h-9 w-full rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    >
      <option value="">{isLoading ? "Loading sprints…" : "Select a sprint…"}</option>
      {sprints?.map((sprint) => (
        <option key={sprint.id} value={sprint.id}>
          {sprint.name}
        </option>
      ))}
    </select>
  );
}
