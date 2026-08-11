"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { createWhiteboard, deleteWhiteboard, listWhiteboardTemplates, listWhiteboards } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import { useAppContext } from "@/lib/app-context/AppContext";
import { cn } from "@/lib/utils";
import { formatIsoDateTime, panelClassName, textInputClassName } from "./collab-ui";

export function WhiteboardsListPageClient() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [name, setName] = useState("");
  const [isPrivate, setIsPrivate] = useState(false);
  const [templateId, setTemplateId] = useState("");
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);

  const whiteboardsQuery = useQuery({
    queryKey: collabKeys.whiteboards(workspaceId),
    queryFn: listWhiteboards,
  });
  const templatesQuery = useQuery({
    queryKey: collabKeys.whiteboardTemplates(workspaceId),
    queryFn: listWhiteboardTemplates,
  });
  const createMutation = useMutation({
    mutationFn: createWhiteboard,
    onSuccess: (whiteboard) => {
      setName("");
      setIsPrivate(false);
      setTemplateId("");
      void queryClient.invalidateQueries({ queryKey: collabKeys.whiteboardsRoot(workspaceId) });
      router.push(`/app/whiteboards/${whiteboard.id}`);
    },
  });
  const deleteMutation = useMutation({
    mutationFn: deleteWhiteboard,
    onSuccess: () => {
      setPendingDeleteId(null);
      void queryClient.invalidateQueries({ queryKey: collabKeys.whiteboardsRoot(workspaceId) });
    },
  });
  const mutationError = createMutation.error ?? deleteMutation.error;

  function submitWhiteboard(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    createMutation.mutate({ name: name.trim() || "Untitled whiteboard", isPrivate, templateId: templateId || null });
  }

  const whiteboards = whiteboardsQuery.data ?? [];

  return (
    <section aria-labelledby="whiteboards-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Whiteboards</p>
          <h1 id="whiteboards-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Whiteboards
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Realtime collaborative canvases: shapes, connectors, sticky notes, text, images and task/document links.
          </p>
        </div>
        <a href="#new-whiteboard" className={buttonStyles({ variant: "primary", size: "sm" })}>
          New whiteboard
        </a>
      </div>

      {mutationError ? (
        <p role="alert" className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300">
          {(mutationError as Error).message}
        </p>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
        <section className={cn(panelClassName, "overflow-hidden")} aria-labelledby="whiteboard-list-title">
          <header className="border-b border-border p-4">
            <h2 id="whiteboard-list-title" className="text-sm font-semibold">
              Whiteboard library
            </h2>
          </header>
          <div className="overflow-x-auto">
            <table className="min-w-full text-left text-sm">
              <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-4 py-3 font-semibold">Name</th>
                  <th className="px-4 py-3 font-semibold">Visibility</th>
                  <th className="px-4 py-3 font-semibold">Updated</th>
                  <th className="px-4 py-3 text-right font-semibold">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {whiteboards.map((whiteboard) => (
                  <tr key={whiteboard.id}>
                    <td className="px-4 py-3">
                      <Link
                        href={`/app/whiteboards/${whiteboard.id}`}
                        className="font-semibold text-foreground underline-offset-4 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                      >
                        {whiteboard.name}
                      </Link>
                      {whiteboard.linkedResourceType ? (
                        <span className="ml-2 text-xs text-muted-foreground">
                          Linked to {whiteboard.linkedResourceType}
                        </span>
                      ) : null}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          "rounded-full px-2.5 py-1 text-xs font-semibold",
                          whiteboard.isPrivate ? "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200" : "bg-primary/10 text-primary",
                        )}
                      >
                        {whiteboard.isPrivate ? "Private" : "Shared"}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{formatIsoDateTime(whiteboard.updatedAtUtc)}</td>
                    <td className="px-4 py-3 text-right">
                      {pendingDeleteId === whiteboard.id ? (
                        <span className="inline-flex flex-wrap items-center justify-end gap-2">
                          <span className="text-xs text-muted-foreground">Delete for everyone?</span>
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            className="border-red-300 text-red-700 dark:border-red-900 dark:text-red-400"
                            disabled={deleteMutation.isPending}
                            onClick={() => deleteMutation.mutate(whiteboard.id)}
                          >
                            Confirm
                          </Button>
                          <Button type="button" size="sm" variant="ghost" onClick={() => setPendingDeleteId(null)}>
                            Cancel
                          </Button>
                        </span>
                      ) : (
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          className="text-red-600 hover:text-red-700 dark:text-red-400"
                          aria-label={`Delete ${whiteboard.name}`}
                          onClick={() => setPendingDeleteId(whiteboard.id)}
                        >
                          Delete
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {whiteboardsQuery.isLoading ? (
              <p className="p-4 text-sm text-muted-foreground">Loading whiteboards…</p>
            ) : whiteboards.length === 0 ? (
              <EmptyState className="m-4" title="No whiteboards yet" description="Use the “New whiteboard” form to start your first canvas." />
            ) : null}
          </div>
        </section>

        <form id="new-whiteboard" onSubmit={submitWhiteboard} className={cn(panelClassName, "p-4")} aria-labelledby="new-whiteboard-title">
          <h2 id="new-whiteboard-title" className="text-sm font-semibold">
            Create whiteboard
          </h2>
          <div className="mt-4 grid gap-3">
            <label className="grid gap-1 text-xs font-medium">
              Name
              <input value={name} onChange={(event) => setName(event.target.value)} className={textInputClassName} placeholder="Sprint planning" />
            </label>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={isPrivate} onChange={(event) => setIsPrivate(event.target.checked)} className="size-4 rounded border-border accent-[var(--primary)]" />
              Private to me
            </label>
            <label className="grid gap-1 text-xs font-medium">
              Start from template
              <select value={templateId} onChange={(event) => setTemplateId(event.target.value)} className={textInputClassName}>
                <option value="">Blank whiteboard</option>
                {(templatesQuery.data ?? []).map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
            </label>
            <Button type="submit" size="sm" disabled={createMutation.isPending}>
              New whiteboard
            </Button>
          </div>
        </form>
      </div>
    </section>
  );
}
