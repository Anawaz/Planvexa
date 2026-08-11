"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import {
  createDashboard,
  deleteDashboard,
  listDashboards,
  updateDashboard,
} from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";

export function DashboardListPageClient() {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [isPrivate, setIsPrivate] = useState(false);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState("");
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);
  const dashboardsQuery = useQuery({
    queryKey: planningKeys.dashboards(),
    queryFn: listDashboards,
  });
  const invalidateDashboards = () =>
    queryClient.invalidateQueries({ queryKey: planningKeys.dashboards() });
  const createDashboardMutation = useMutation({
    mutationFn: createDashboard,
    onSuccess: () => {
      setName("");
      setIsPrivate(false);
      void invalidateDashboards();
    },
  });
  const renameDashboardMutation = useMutation({
    mutationFn: ({ id, name: nextName }: { id: string; name: string }) =>
      updateDashboard(id, { name: nextName }),
    onSuccess: () => {
      setRenamingId(null);
      void invalidateDashboards();
    },
  });
  const deleteDashboardMutation = useMutation({
    mutationFn: deleteDashboard,
    onSuccess: () => {
      setPendingDeleteId(null);
      void invalidateDashboards();
    },
  });
  const mutationError =
    createDashboardMutation.error ??
    renameDashboardMutation.error ??
    deleteDashboardMutation.error;

  function submitDashboard(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!name.trim()) {
      return;
    }

    createDashboardMutation.mutate({
      name: name.trim(),
      isPrivate,
      widgets: [
        { type: "Overdue", config: { title: "Overdue tasks" } },
        { type: "Completed", config: { title: "Completed tasks" } },
        { type: "Workload", config: { title: "Scheduled workload" } },
      ],
    });
  }

  return (
    <section aria-labelledby="dashboards-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Reporting</p>
          <h1 id="dashboards-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Dashboards
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Saved dashboard definitions with widget configs and server-side data queries.
          </p>
        </div>
        <Link href="/app/reports/time" className={buttonStyles({ variant: "outline", size: "sm" })}>
          Time reports
        </Link>
      </div>

      {mutationError ? (
        <p
          role="alert"
          className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          This dashboard change could not be saved: {(mutationError as Error).message}
        </p>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
        <section className="grid gap-4 md:grid-cols-2" aria-label="Dashboard list">
          {dashboardsQuery.isLoading ? (
            <p className="rounded-[var(--radius)] border border-border bg-card p-4 text-sm text-muted-foreground">
              Loading dashboards…
            </p>
          ) : (dashboardsQuery.data ?? []).length === 0 ? (
            <EmptyState
              className="md:col-span-2"
              title="No dashboards yet"
              description="Create one with the form beside this list, then add widgets to watch throughput, workload or due dates."
            />
          ) : (
            dashboardsQuery.data?.map((dashboard) => (
              <article
                key={dashboard.id}
                className="rounded-[var(--radius)] border border-border bg-card p-5 shadow-sm"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    {renamingId === dashboard.id ? (
                      <form
                        className="flex flex-wrap items-center gap-2"
                        onSubmit={(event) => {
                          event.preventDefault();
                          if (!renameDraft.trim()) {
                            return;
                          }

                          renameDashboardMutation.mutate({
                            id: dashboard.id,
                            name: renameDraft.trim(),
                          });
                        }}
                      >
                        <label className="sr-only" htmlFor={`rename-${dashboard.id}`}>
                          Dashboard name
                        </label>
                        <input
                          id={`rename-${dashboard.id}`}
                          value={renameDraft}
                          autoFocus
                          onChange={(event) => setRenameDraft(event.target.value)}
                          className="h-9 min-w-0 flex-1 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                        />
                        <Button
                          type="submit"
                          size="sm"
                          disabled={!renameDraft.trim() || renameDashboardMutation.isPending}
                        >
                          Save
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          onClick={() => setRenamingId(null)}
                        >
                          Cancel
                        </Button>
                      </form>
                    ) : (
                      <h2 className="truncate text-lg font-semibold">{dashboard.name}</h2>
                    )}
                    <p className="mt-2 text-sm text-muted-foreground">
                      {dashboard.widgetCount} widgets ·{" "}
                      {dashboard.isPrivate ? "Private" : "Shared"}
                    </p>
                  </div>
                  <span className="rounded-full bg-primary/10 px-2.5 py-1 text-xs font-semibold text-primary">
                    {dashboard.id}
                  </span>
                </div>
                <div className="mt-4 flex flex-wrap items-center gap-2">
                  <Link
                    href={`/app/dashboards/${dashboard.id}`}
                    className={buttonStyles({ variant: "primary", size: "sm" })}
                  >
                    Open dashboard
                  </Link>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() => {
                      setRenamingId(dashboard.id);
                      setRenameDraft(dashboard.name);
                      setPendingDeleteId(null);
                    }}
                  >
                    Rename
                  </Button>
                  {pendingDeleteId === dashboard.id ? (
                    <>
                      <Button
                        type="button"
                        size="sm"
                        variant="outline"
                        className="border-red-300 text-red-700 dark:border-red-900 dark:text-red-400"
                        disabled={deleteDashboardMutation.isPending}
                        onClick={() => deleteDashboardMutation.mutate(dashboard.id)}
                      >
                        Confirm delete
                      </Button>
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => setPendingDeleteId(null)}
                      >
                        Keep
                      </Button>
                    </>
                  ) : (
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      className="text-red-600 hover:text-red-700 dark:text-red-400"
                      onClick={() => setPendingDeleteId(dashboard.id)}
                    >
                      Delete
                    </Button>
                  )}
                </div>
              </article>
            ))
          )}
        </section>

        <form
          onSubmit={submitDashboard}
          className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
        >
          <h2 className="text-sm font-semibold">Create dashboard</h2>
          <p className="mt-1 text-xs text-muted-foreground">
            New dashboards start with a useful set of widgets.
          </p>
          <div className="mt-4 grid gap-3">
            <label className="grid gap-1 text-xs font-medium">
              Dashboard name
              <input
                value={name}
                onChange={(event) => setName(event.target.value)}
                className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              />
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={isPrivate}
                onChange={(event) => setIsPrivate(event.target.checked)}
                className="size-4 rounded border-border accent-[var(--primary)]"
              />
              Private to me
            </label>
            <Button type="submit" size="sm" disabled={createDashboardMutation.isPending}>
              Create dashboard
            </Button>
          </div>
        </form>
      </div>
    </section>
  );
}
