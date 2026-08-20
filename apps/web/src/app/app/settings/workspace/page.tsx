"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { PageHeader, panelClassName, textInputClassName } from "@/components/admin/admin-ui";
import { Button, buttonStyles } from "@/components/ui/Button";
import { ApiError, apiClient } from "@/lib/api-client";
import { useAppContext } from "@/lib/app-context/AppContext";
import { cn } from "@/lib/utils";

const errorClassName =
  "rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300";

export default function WorkspaceSettingsPage() {
  const { currentWorkspace, isLoading } = useAppContext();

  if (isLoading || !currentWorkspace) {
    return <p className="text-sm text-muted-foreground">Loading workspace…</p>;
  }

  return (
    <section aria-labelledby="workspace-settings-title" className="max-w-3xl space-y-6">
      <PageHeader
        id="workspace-settings-title"
        eyebrow="Workspace"
        title={currentWorkspace.name}
        description="Workspace is the top-level boundary: everything below belongs to this workspace only."
      />

      <dl className={cn(panelClassName, "grid gap-4 p-5 sm:grid-cols-3")}>
        <div>
          <dt className="text-xs uppercase tracking-wide text-muted-foreground">Name</dt>
          <dd className="mt-1 text-sm font-medium">{currentWorkspace.name}</dd>
        </div>
        <div>
          <dt className="text-xs uppercase tracking-wide text-muted-foreground">Slug</dt>
          <dd className="mt-1 font-mono text-sm">{currentWorkspace.slug}</dd>
        </div>
        <div>
          <dt className="text-xs uppercase tracking-wide text-muted-foreground">Your role</dt>
          <dd className="mt-1 text-sm font-medium">{currentWorkspace.role}</dd>
        </div>
      </dl>

      <div className={cn(panelClassName, "flex flex-wrap items-center justify-between gap-3 p-5")}>
        <div>
          <h2 className="text-sm font-semibold">Create a new workspace</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            A separate workspace with its own spaces, members and data. You become its Owner.
          </p>
        </div>
        <Link href="/onboarding" className={buttonStyles({ variant: "secondary" })}>
          New workspace
        </Link>
      </div>

      {currentWorkspace.role === "Owner" ? (
        <DangerZone workspaceId={currentWorkspace.id} slug={currentWorkspace.slug} />
      ) : null}
    </section>
  );
}

function DangerZone({ workspaceId, slug }: { workspaceId: string; slug: string }) {
  const queryClient = useQueryClient();
  const [confirmSlug, setConfirmSlug] = useState("");

  const remove = useMutation({
    mutationFn: () => apiClient.post<void>(`/workspaces/${workspaceId}/delete`, { confirmSlug }),
    onSuccess: () => {
      queryClient.clear();
      // Full navigation so the shell re-bootstraps; it redirects to /onboarding when no workspace is left.
      window.location.assign("/app");
    },
  });

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    remove.mutate();
  }

  return (
    <form
      onSubmit={submit}
      aria-labelledby="danger-zone-title"
      className="space-y-4 rounded-[var(--radius)] border border-red-300 bg-red-50/50 p-5 dark:border-red-900 dark:bg-red-950/30"
    >
      <div>
        <h2 id="danger-zone-title" className="text-lg font-semibold text-red-700 dark:text-red-300">
          Delete this workspace
        </h2>
        <p className="mt-2 text-sm leading-6 text-red-700 dark:text-red-300">
          This permanently deletes every space, list, task, document, comment, time entry and file in
          this workspace, for every member. It cannot be undone and there is no backup to restore
          from.
        </p>
      </div>

      <label htmlFor="confirm-slug" className="grid gap-2 text-sm font-medium">
        Type <span className="font-mono">{slug}</span> to confirm
        <input
          id="confirm-slug"
          value={confirmSlug}
          onChange={(event) => setConfirmSlug(event.target.value)}
          autoComplete="off"
          className={textInputClassName}
        />
      </label>

      {remove.error ? (
        <p role="alert" className={errorClassName}>
          {remove.error instanceof ApiError ? remove.error.message : "Could not delete the workspace."}
        </p>
      ) : null}

      <Button
        type="submit"
        disabled={confirmSlug !== slug || remove.isPending}
        className="bg-red-600 text-white shadow-sm [@media(hover:hover)]:hover:bg-red-700"
      >
        {remove.isPending ? "Deleting…" : "Delete workspace permanently"}
      </Button>
    </form>
  );
}
