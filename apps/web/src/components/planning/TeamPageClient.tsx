"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { useMemberDirectory } from "@/lib/members";
import { getTeamWorkload } from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type { TeamWorkloadMember } from "@/lib/planning/types";
import { cn } from "@/lib/utils";
import { addDays, formatHours, formatLongDate, startOfUtcWeek } from "./helpers";

function MemberChip({ member }: { member: TeamWorkloadMember }) {
  const directory = useMemberDirectory();
  const utilization = member.capacityHours ? Math.round((member.scheduledHours / member.capacityHours) * 100) : 0;

  return (
    <li
      className={cn(
        "flex items-center gap-2 rounded-full border border-border bg-background px-3 py-1.5 text-xs",
        member.isOverAllocated && "border-amber-400 bg-amber-50 dark:bg-amber-950/30",
      )}
    >
      <Avatar
        avatarUrl={directory.getAvatarUrl(member.userId)}
        initials={directory.getInitials(member.userId)}
        className="grid size-6 place-items-center rounded-full bg-muted text-[0.65rem] font-semibold"
      />
      <span className="font-medium">{directory.getLabel(member.userId)}</span>
      <span className="text-muted-foreground">{formatHours(member.scheduledHours)} scheduled</span>
      {member.isOverAllocated ? (
        <span className="font-semibold text-amber-700 dark:text-amber-300">{utilization}%</span>
      ) : null}
    </li>
  );
}

/**
 * Team view -- work grouped by Team instead of flat per-individual. Backend: GET
 * /api/v1/views/team (TeamWorkloadService), which reuses WorkloadService's per-member computation and
 * groups it by Team membership (Tenancy's Team/TeamMembership, read cross-module via
 * ITeamDirectoryQuery). Same Admin+ "manage" authorization as the existing Workload view.
 */
export function TeamPageClient() {
  const [rangeStart, setRangeStart] = useState(() => startOfUtcWeek(new Date()));
  const rangeEnd = useMemo(() => addDays(rangeStart, 13), [rangeStart]);
  const params = useMemo(
    () => ({ from: rangeStart.toISOString(), to: rangeEnd.toISOString() }),
    [rangeEnd, rangeStart],
  );
  const teamQuery = useQuery({
    queryKey: planningKeys.team(params),
    queryFn: () => getTeamWorkload(params),
  });
  const rows = teamQuery.data ?? [];

  return (
    <section aria-labelledby="team-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Views</p>
          <h1 id="team-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Team
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Each Team&apos;s members and their current workload at a glance.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2" aria-label="Team date range">
          <Button type="button" variant="outline" size="sm" onClick={() => setRangeStart((c) => addDays(c, -14))}>
            Previous range
          </Button>
          <Button type="button" variant="secondary" size="sm" onClick={() => setRangeStart(startOfUtcWeek(new Date()))}>
            Current range
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={() => setRangeStart((c) => addDays(c, 14))}>
            Next range
          </Button>
        </div>
      </div>

      <p className="text-xs text-muted-foreground">
        {formatLongDate(rangeStart)} – {formatLongDate(rangeEnd)}
      </p>

      {teamQuery.isLoading ? (
        <p className="p-4 text-sm text-muted-foreground">Loading teams…</p>
      ) : rows.length === 0 ? (
        <EmptyState
          className="m-4"
          title="No teams yet"
          description="Create a Team under Members to see its workload here."
        />
      ) : (
        <div className="space-y-4">
          {rows.map((row) => (
            <article key={row.teamId} className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 className="text-lg font-semibold">{row.teamName}</h2>
                <span className="text-xs text-muted-foreground">
                  {formatHours(row.scheduledHours)} scheduled · {formatHours(row.capacityHours)} capacity
                </span>
              </div>
              {row.members.length === 0 ? (
                <p className="mt-3 text-sm text-muted-foreground">No members yet.</p>
              ) : (
                <ul className="mt-3 flex flex-wrap gap-2">
                  {row.members.map((member) => (
                    <MemberChip key={member.userId} member={member} />
                  ))}
                </ul>
              )}
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
