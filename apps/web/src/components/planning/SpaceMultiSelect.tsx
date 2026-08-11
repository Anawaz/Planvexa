"use client";

import { useQuery } from "@tanstack/react-query";
import { listSpaces } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";

type SpaceMultiSelectProps = {
  value: string[];
  onChange: (spaceIds: string[]) => void;
  disabled?: boolean;
};

/**
 * Checkbox list of the workspace's Spaces, so curating a Portfolio's membership never requires
 * pasting a raw id (spec: normal users must never enter a raw UUID/database identifier).
 */
export function SpaceMultiSelect({ value, onChange, disabled }: SpaceMultiSelectProps) {
  const { data: spaces, isPending } = useQuery({ queryKey: workKeys.spaces(), queryFn: listSpaces });

  function toggle(spaceId: string) {
    onChange(value.includes(spaceId) ? value.filter((id) => id !== spaceId) : [...value, spaceId]);
  }

  return (
    <ul className="mt-1 max-h-40 space-y-1 overflow-auto rounded-lg border border-border p-2">
      {isPending ? <li className="text-xs text-muted-foreground">Loading spaces…</li> : null}
      {(spaces ?? []).map((space) => (
        <li key={space.id}>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={value.includes(space.id)}
              disabled={disabled}
              onChange={() => toggle(space.id)}
              className="size-4 rounded border-border accent-[var(--primary)]"
            />
            {space.name}
          </label>
        </li>
      ))}
      {!isPending && (spaces ?? []).length === 0 ? (
        <li className="text-xs text-muted-foreground">No spaces in this workspace yet.</li>
      ) : null}
    </ul>
  );
}
