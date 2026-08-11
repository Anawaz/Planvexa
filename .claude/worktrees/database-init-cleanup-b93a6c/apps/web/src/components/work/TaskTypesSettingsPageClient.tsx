"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import { createTaskType, listTaskTypes, updateTaskType } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";

/**  . uc(w)orkspace-configurable task types, following the same CRUD-settings-page pattern as
 * PlanningSettingsPageClient (working days/holidays/leave) and Planning's Estimate concept. */
export function TaskTypesSettingsPageClient() {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [color, setColor] = useState("#2b7fff");
  const typesQuery = useQuery({ queryKey: workKeys.taskTypes(), queryFn: listTaskTypes });

  const createMutation = useMutation({
    mutationFn: () => createTaskType({ name: name.trim(), color }),
    onSuccess: () => {
      setName("");
      void queryClient.invalidateQueries({ queryKey: workKeys.taskTypes() });
    },
  });
  const renameMutation = useMutation({
    mutationFn: (input: { id: string; name: string; color: string }) =>
      updateTaskType(input.id, { name: input.name, color: input.color }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: workKeys.taskTypes() }),
  });

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (name.trim()) {
      createMutation.mutate();
    }
  }

  return (
    <section aria-labelledby="task-types-settings-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Settings</p>
        <h1 id="task-types-settings-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Task types
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Configure the task types available in this workspace (e.g. Bug, Milestone). The built-in
          &quot;Task&quot; type cannot be removed.
        </p>
      </div>

      <section className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm" aria-labelledby="task-types-list-title">
        <h2 id="task-types-list-title" className="text-sm font-semibold">
          Types
        </h2>
        <form onSubmit={submit} className="mt-4 grid gap-3 sm:grid-cols-[1fr_auto_auto]">
          <label className="grid gap-1 text-xs font-medium">
            Name
            <input
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="e.g. Bug"
              className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Color
            <input
              type="color"
              value={color}
              onChange={(event) => setColor(event.target.value)}
              className="h-10 w-14 rounded-lg border border-border bg-background"
            />
          </label>
          <Button type="submit" size="sm" className="self-end" disabled={createMutation.isPending}>
            Add type
          </Button>
        </form>

        <div className="mt-4 divide-y divide-border rounded-xl border border-border">
          {typesQuery.isLoading ? (
            <p className="p-3 text-sm text-muted-foreground">Loading task types…</p>
          ) : (
            typesQuery.data?.map((type) => (
              <div key={type.id} className="flex items-center justify-between gap-3 p-3">
                <div className="flex items-center gap-2">
                  <span
                    className="inline-block size-3 rounded-full"
                    style={{ backgroundColor: type.color }}
                  />
                  <p className="text-sm font-semibold">
                    {type.name}
                    {type.isBuiltIn ? (
                      <span className="ml-2 text-xs font-normal text-muted-foreground">(built-in)</span>
                    ) : null}
                  </p>
                </div>
                {!type.isBuiltIn ? (
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={renameMutation.isPending}
                    onClick={() => {
                      const next = window.prompt("Rename task type", type.name);
                      if (next && next.trim()) {
                        renameMutation.mutate({ id: type.id, name: next.trim(), color: type.color });
                      }
                    }}
                  >
                    Rename
                  </Button>
                ) : null}
              </div>
            ))
          )}
        </div>
      </section>
    </section>
  );
}
