"use client";

import { useQuery } from "@tanstack/react-query";
import { cn } from "@/lib/utils";
import { listMyTasks } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";

type TaskSelectProps = {
  id?: string;
  value: string;
  onChange: (taskId: string) => void;
  disabled?: boolean;
  className?: string;
  placeholder?: string;
  "aria-label"?: string;
};

/**
 * A workspace-scoped task dropdown sourced from the caller's accessible tasks, presenting task
 * titles and submitting the internal task id — so no screen requires pasting a raw GUID (ADR 0015).
 * Server authorization still validates the chosen task on submit.
 */
export function TaskSelect({
  id,
  value,
  onChange,
  disabled,
  className,
  placeholder = "Select a task…",
  ...rest
}: TaskSelectProps) {
  const { data, isPending } = useQuery({
    queryKey: workKeys.myTasks(),
    queryFn: () => listMyTasks(),
  });
  const tasks = data ?? [];

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
      <option value="">{placeholder}</option>
      {tasks.map((task) => (
        <option key={task.id} value={task.id}>
          {task.title}
        </option>
      ))}
    </select>
  );
}
