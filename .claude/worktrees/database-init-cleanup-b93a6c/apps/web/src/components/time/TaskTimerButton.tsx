"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { getActiveTimer } from "@/lib/time/client";
import { formatDuration } from "@/lib/time/format";
import { startTimerOffline, stopTimerOffline } from "@/lib/time/offlineMutations";
import { timeKeys } from "@/lib/time/queries";
import { useCurrentUserId } from "@/lib/members";

export function TaskTimerButton({
  taskId,
  taskTitle,
}: {
  taskId: string;
  taskTitle: string;
}) {
  const queryClient = useQueryClient();
  const currentUserId = useCurrentUserId();
  const [now, setNow] = useState(() => Date.now());
  const activeTimerQuery = useQuery({
    queryKey: timeKeys.activeTimer(),
    queryFn: getActiveTimer,
    refetchInterval: 30_000,
  });
  const activeTimer = activeTimerQuery.data;
  const isCurrentTaskTimer = activeTimer?.taskId === taskId;

  useEffect(() => {
    const intervalId = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(intervalId);
  }, []);

  const invalidateTime = () => queryClient.invalidateQueries({ queryKey: timeKeys.all });
  const startMutation = useMutation({
    mutationFn: () => startTimerOffline({ taskId, description: taskTitle }, currentUserId ?? ""),
    onSuccess: (timer) => {
      // No live refetch while offline — set the result directly so the UI reflects it either way.
      queryClient.setQueryData(timeKeys.activeTimer(), timer);
      void invalidateTime();
    },
  });
  const stopMutation = useMutation({
    mutationFn: () => stopTimerOffline(activeTimer),
    onSuccess: () => {
      queryClient.setQueryData(timeKeys.activeTimer(), null);
      void invalidateTime();
    },
  });
  const isBusy = startMutation.isPending || stopMutation.isPending;
  const elapsedSeconds = activeTimer
    ? Math.max(0, Math.floor((now - new Date(activeTimer.startedAtUtc).getTime()) / 1000))
    : 0;

  if (isCurrentTaskTimer) {
    return (
      <div className="flex flex-wrap items-center gap-2">
        <span className="rounded-full bg-muted px-3 py-1 text-xs font-medium text-muted-foreground">
          Tracking · {formatDuration(elapsedSeconds)}
        </span>
        <Button
          type="button"
          size="sm"
          variant="primary"
          disabled={isBusy}
          onClick={() => stopMutation.mutate()}
        >
          Stop timer
        </Button>
      </div>
    );
  }

  return (
    <Button
      type="button"
      size="sm"
      variant="outline"
      disabled={Boolean(activeTimer) || isBusy}
      title={activeTimer ? "Stop the active timer before starting another one." : undefined}
      onClick={() => startMutation.mutate()}
    >
      Start timer
    </Button>
  );
}
