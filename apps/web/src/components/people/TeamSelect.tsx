"use client";

import { cn } from "@/lib/utils";
import { useTeams } from "@/lib/members";

type TeamSelectProps = {
  id?: string;
  value: string;
  onChange: (teamId: string) => void;
  /** When true, prepends an empty "no team" option (routing/filter fields are optional). */
  includeAny?: boolean;
  anyLabel?: string;
  disabled?: boolean;
  className?: string;
  "aria-label"?: string;
};

/**
 * A workspace-scoped team dropdown. Presents team names and submits the internal team id, so no
 * screen requires pasting a raw GUID (spec: normal users must never enter a raw UUID). Teams come
 * from the current Workspace only via {@link useTeams}.
 */
export function TeamSelect({
  id,
  value,
  onChange,
  includeAny = false,
  anyLabel = "No team",
  disabled,
  className,
  ...rest
}: TeamSelectProps) {
  const { data, isPending } = useTeams();
  const teams = data ?? [];

  return (
    <select
      id={id}
      value={value}
      disabled={disabled || isPending}
      onChange={(event) => onChange(event.target.value)}
      aria-label={rest["aria-label"]}
      className={cn(
        "h-10 rounded-lg border border-border bg-background px-3 text-sm shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
        className,
      )}
    >
      {includeAny ? <option value="">{anyLabel}</option> : null}
      {!includeAny && !value ? <option value="">Select a team…</option> : null}
      {teams.map((team) => (
        <option key={team.id} value={team.id}>
          {team.name}
          {team.isArchived ? " (archived)" : ""}
        </option>
      ))}
    </select>
  );
}
