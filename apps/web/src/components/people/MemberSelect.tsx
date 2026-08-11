"use client";

import { cn } from "@/lib/utils";
import { useMembers } from "@/lib/members";

type MemberSelectProps = {
  id?: string;
  value: string;
  onChange: (userId: string) => void;
  /** When true, prepends an empty "any member" option (for filters). */
  includeAny?: boolean;
  anyLabel?: string;
  disabled?: boolean;
  className?: string;
  "aria-label"?: string;
};

/**
 * A workspace-scoped member dropdown. Presents human-readable names/emails and submits the internal
 * user id, so no screen requires pasting a raw GUID (ADR 0015). Members come from the current
 * Workspace only via {@link useMembers}.
 */
export function MemberSelect({
  id,
  value,
  onChange,
  includeAny = false,
  anyLabel = "Anyone",
  disabled,
  className,
  ...rest
}: MemberSelectProps) {
  const { data, isPending } = useMembers();
  const members = data ?? [];

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
      {!includeAny && !value ? <option value="">Select a member…</option> : null}
      {members.map((member) => (
        <option key={member.userId} value={member.userId}>
          {member.displayName ?? member.email ?? member.userId}
          {member.status !== "Active" ? " (inactive)" : ""}
        </option>
      ))}
    </select>
  );
}
