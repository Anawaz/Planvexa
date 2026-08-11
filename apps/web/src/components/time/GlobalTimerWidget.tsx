"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useCurrentUserId } from "@/lib/members";
import { getActiveTimer, pauseTimer, resumeTimer } from "@/lib/time/client";
import { formatDuration } from "@/lib/time/format";
import { startTimerOffline, stopTimerOffline } from "@/lib/time/offlineMutations";
import { timeKeys } from "@/lib/time/queries";
import type { ActiveTimer } from "@/lib/time/types";

// Server-authoritative: while paused, freeze the display at pausedAtUtc instead of ticking against
// `now`, and always exclude the accumulated pausedSeconds (mirrors TimeEntry.Stop's calculation).
function elapsedSeconds(timer: ActiveTimer, now: number) {
  const reference = timer.isPaused && timer.pausedAtUtc ? new Date(timer.pausedAtUtc).getTime() : now;
  return Math.max(0, Math.floor((reference - new Date(timer.startedAtUtc).getTime()) / 1000) - timer.pausedSeconds);
}

export function GlobalTimerWidget() {
  const queryClient = useQueryClient();
  const currentUserId = useCurrentUserId();
  const { currentWorkspace } = useAppContext();
  // Guests have no time-tracking permission server-side (TimeAuthorizer 403s) — don't offer the
  // widget or query for a capability the current role can't use.
  const canTrackTime = currentWorkspace?.role !== "Guest";
  const [now, setNow] = useState(() => Date.now());
  const [lastStoppedDuration, setLastStoppedDuration] = useState<number | null>(null);
  const activeTimerQuery = useQuery({
    queryKey: timeKeys.activeTimer(),
    queryFn: getActiveTimer,
    refetchInterval: 30_000,
    enabled: canTrackTime,
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
  // ponytail: pause/resume call the API directly rather than going through the offline mutation
  // queue like start/stop -- they're a control toggle on an already-running timer, not a create, so
  // there's nothing to replay if the request never landed. Add offline queueing if that stops holding.
  const pauseMutation = useMutation({
    mutationFn: () => pauseTimer(),
    onSuccess: (timer) => {
      queryClient.setQueryData(timeKeys.activeTimer(), timer);
      void invalidateTime();
    },
  });
  const resumeMutation = useMutation({
    mutationFn: () => resumeTimer(),
    onSuccess: (timer) => {
      queryClient.setQueryData(timeKeys.activeTimer(), timer);
      void invalidateTime();
    },
  });
  const isBusy = startMutation.isPending || stopMutation.isPending || pauseMutation.isPending || resumeMutation.isPending;

  if (!canTrackTime) {
    return null;
  }

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
  const elapsed = elapsedSeconds(activeTimer, now);

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
          {activeTimer.isPaused ? "Paused" : "Running"} · {formatDuration(elapsed)}
        </p>
      </div>
      <div className="ml-auto flex items-center gap-1">
        {activeTimer.isPaused ? (
          <Button
            type="button"
            size="sm"
            variant="outline"
            className="h-8 px-2 text-xs"
            disabled={isBusy}
            onClick={() => resumeMutation.mutate()}
          >
            Resume
          </Button>
        ) : (
          <Button
            type="button"
            size="sm"
            variant="outline"
            className="h-8 px-2 text-xs"
            disabled={isBusy}
            onClick={() => pauseMutation.mutate()}
          >
            Pause
          </Button>
        )}
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
