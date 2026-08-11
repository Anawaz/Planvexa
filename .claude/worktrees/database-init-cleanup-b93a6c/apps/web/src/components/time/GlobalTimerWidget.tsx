"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { useCurrentUserId } from "@/lib/members";
import { getActiveTimer } from "@/lib/time/client";
import { formatDuration } from "@/lib/time/format";
import { startTimerOffline, stopTimerOffline } from "@/lib/time/offlineMutations";
import { timeKeys } from "@/lib/time/queries";

function elapsedFromServerStart(startedAtUtc: string, now: number) {
  return Math.max(0, Math.floor((now - new Date(startedAtUtc).getTime()) / 1000));
}

export function GlobalTimerWidget() {
  const queryClient = useQueryClient();
  const currentUserId = useCurrentUserId();
  const [now, setNow] = useState(() => Date.now());
  const [lastStoppedDuration, setLastStoppedDuration] = useState<number | null>(null);
  const activeTimerQuery = useQuery({
    queryKey: timeKeys.activeTimer(),
    queryFn: getActiveTimer,
    refetchInterval: 30_000,
  });
  const activeTimer = activeTimerQuery.data;

  useEffect(() => {
    const intervalId = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(intervalId);
  }, []);

  const invalidateTime = () => queryClient.invalidateQueries({ queryKey: timeKeys.all });
  const startMutation = useMutation({
    mutationFn: () => startTimerOffline({ description: "Quick timer" }, currentUserId ?? ""),
    onSuccess: (timer) => {
      queryClient.setQueryData(timeKeys.activeTimer(), timer);
      setLastStoppedDuration(null);
      void invalidateTime();
    },
  });
  const stopMutation = useMutation({
    mutationFn: () => stopTimerOffline(activeTimer),
    onSuccess: (entry) => {
      queryClient.setQueryData(timeKeys.activeTimer(), null);
      setLastStoppedDuration(entry?.durationSeconds ?? null);
      void invalidateTime();
    },
  });
  const isBusy = startMutation.isPending || stopMutation.isPending;

  if (!activeTimer) {
    return (
      <div className="hidden shrink-0 items-center gap-2 rounded-xl border border-border bg-card px-2 py-1.5 text-xs shadow-sm md:flex">
        <span className="whitespace-nowrap text-muted-foreground">
          {lastStoppedDuration === null
            ? "No timer running"
            : `Stopped at ${formatDuration(lastStoppedDuration)}`}
        </span>
        <Button
          type="button"
          size="sm"
          variant="outline"
          className="h-8 px-2 text-xs"
          disabled={isBusy}
          onClick={() => startMutation.mutate()}
        >
          Start timer
        </Button>
      </div>
    );
  }

  // Server-authoritative timer: the UI recomputes elapsed from startedAtUtc each tick;
  // it never accumulates client-side seconds, and Stop displays the server duration.
  const elapsedSeconds = elapsedFromServerStart(activeTimer.startedAtUtc, now);

  return (
    <section
      aria-label="Active time tracker"
      className="hidden max-w-xs items-center gap-2 rounded-xl border border-border bg-card px-2 py-1.5 text-xs shadow-sm md:flex"
    >
      <div className="min-w-0">
        {/* The API's TimeEntryDto has no taskTitle, so a task timer would otherwise read
            "Quick timer"; TaskTimerButton starts it with the task title as the description. */}
        <p className="truncate font-medium">
          {activeTimer.taskTitle ?? activeTimer.description ?? "Quick timer"}
        </p>
        <p className="text-muted-foreground">
          Running · {formatDuration(elapsedSeconds)}
        </p>
      </div>
      <div className="ml-auto flex items-center gap-1">
        <Button
          type="button"
          size="sm"
          variant="primary"
          className="h-8 px-2 text-xs"
          disabled={isBusy}
          onClick={() => stopMutation.mutate()}
        >
          Stop
        </Button>
      </div>
    </section>
  );
}
