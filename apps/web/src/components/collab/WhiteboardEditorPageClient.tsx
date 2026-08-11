"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { createWhiteboardTemplate, getWhiteboard, updateWhiteboard } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import type { Whiteboard } from "@/lib/collab/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useRecordRecentView } from "@/lib/recent/useRecordRecentView";
import { createWhiteboardProvider } from "@/lib/collab/hocuspocusProvider";
import { cn } from "@/lib/utils";
import { WhiteboardCanvas } from "./whiteboard/WhiteboardCanvas";
import { formatIsoDateTime, panelClassName, textInputClassName } from "./collab-ui";

/** Mirrors canEditDocument in DocumentEditorPageClient — UI affordance only, the real enforcement is
 * server-side (WhiteboardsAuthorizer + the can-collaborate check gating the Hocuspocus room itself). */
function canEditWhiteboard(whiteboard: Whiteboard, role: string | undefined, currentUserId: string | undefined) {
  if (!role || role === "Guest") return false;
  if (!whiteboard.isPrivate) return true;
  return whiteboard.ownerUserId === currentUserId || role === "Admin" || role === "Owner";
}

function WhiteboardEditorWorkspace({ whiteboard }: { whiteboard: Whiteboard }) {
  const queryClient = useQueryClient();
  const { workspaceId = "", currentWorkspace, currentUserId } = useAppContext();
  const [name, setName] = useState(whiteboard.name);
  const [isPrivate, setIsPrivate] = useState(whiteboard.isPrivate);
  const [statusMessage, setStatusMessage] = useState("");
  const [templateName, setTemplateName] = useState("");
  const canEdit = canEditWhiteboard(whiteboard, currentWorkspace?.role, currentUserId);

  // HocuspocusProvider is an external connection object, not React state; useMemo just scopes its
  // lifetime to (whiteboardId, workspaceId) and the effect below tears it down on change/unmount.
  const provider = useMemo(() => createWhiteboardProvider(whiteboard.id, workspaceId), [whiteboard.id, workspaceId]);

  useEffect(() => {
    return () => {
      provider.destroy();
    };
  }, [provider]);

  const saveMetaMutation = useMutation({
    mutationFn: () => updateWhiteboard(whiteboard.id, { name, isPrivate: whiteboard.linkedResourceType ? undefined : isPrivate }),
    onSuccess: (updated) => {
      setStatusMessage("Saved.");
      queryClient.setQueryData(collabKeys.whiteboard(workspaceId, whiteboard.id), updated);
      void queryClient.invalidateQueries({ queryKey: collabKeys.whiteboards(workspaceId) });
    },
  });
  const templateMutation = useMutation({
    mutationFn: () => createWhiteboardTemplate(whiteboard.id, templateName.trim() || whiteboard.name),
    onSuccess: () => {
      setStatusMessage("Saved as a reusable template.");
      setTemplateName("");
    },
  });
  const mutationError = saveMetaMutation.error ?? templateMutation.error;

  function submitMeta(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!name.trim()) {
      setStatusMessage("Name is required.");
      return;
    }

    saveMetaMutation.mutate();
  }

  return (
    <section aria-labelledby="whiteboard-editor-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Whiteboards</p>
          <h1 id="whiteboard-editor-title" className="mt-2 text-3xl font-semibold tracking-tight">
            {whiteboard.name}
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Realtime collaborative canvas — every shape you draw is visible live to anyone else with this whiteboard open.
          </p>
        </div>
        <Link href="/app/whiteboards" className={buttonStyles({ variant: "outline", size: "sm" })}>
          Back to whiteboards
        </Link>
      </div>

      {mutationError ? (
        <p role="alert" className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300">
          {(mutationError as Error).message}
        </p>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
        <div>
          <WhiteboardCanvas whiteboardId={whiteboard.id} provider={provider} canEdit={canEdit} />
        </div>

        <aside className="space-y-4">
          <form onSubmit={submitMeta} className={cn(panelClassName, "p-4")}>
            <h2 className="text-sm font-semibold">Whiteboard settings</h2>
            <div className="mt-3 grid gap-3">
              <label className="grid gap-1 text-xs font-medium">
                Name
                <input value={name} onChange={(event) => setName(event.target.value)} className={textInputClassName} />
              </label>
              {whiteboard.linkedResourceType ? (
                <p className="text-xs text-muted-foreground">
                  Linked to a {whiteboard.linkedResourceType} — visibility follows that resource, not a private flag here.
                </p>
              ) : (
                <label className="flex items-center gap-2 text-sm">
                  <input type="checkbox" checked={isPrivate} onChange={(event) => setIsPrivate(event.target.checked)} className="size-4 rounded border-border accent-[var(--primary)]" />
                  {isPrivate ? "Private to owner" : "Shared with workspace"}
                </label>
              )}
              <div className="flex items-center justify-between gap-3">
                {statusMessage ? <p role="status" className="text-xs text-muted-foreground">{statusMessage}</p> : null}
                <Button type="submit" size="sm" disabled={saveMetaMutation.isPending}>
                  Save
                </Button>
              </div>
            </div>
          </form>

          <div className={cn(panelClassName, "p-4")}>
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Updated</p>
            <p className="mt-1 text-sm font-semibold">{formatIsoDateTime(whiteboard.updatedAtUtc)}</p>
            <p className="mt-4 text-xs font-medium uppercase tracking-wide text-muted-foreground">Save as template</p>
            <div className="mt-2 flex gap-2">
              <input
                value={templateName}
                onChange={(event) => setTemplateName(event.target.value)}
                placeholder={whiteboard.name}
                className={cn(textInputClassName, "h-8 flex-1 text-xs")}
              />
              <Button type="button" size="sm" variant="outline" disabled={templateMutation.isPending} onClick={() => templateMutation.mutate()}>
                Save
              </Button>
            </div>
          </div>
        </aside>
      </div>
    </section>
  );
}

export function WhiteboardEditorPageClient({ whiteboardId }: { whiteboardId: string }) {
  useRecordRecentView("whiteboard", whiteboardId);
  const { workspaceId = "" } = useAppContext();
  const whiteboardQuery = useQuery({
    queryKey: collabKeys.whiteboard(workspaceId, whiteboardId),
    queryFn: () => getWhiteboard(whiteboardId),
  });
  const whiteboard = whiteboardQuery.data;

  if (whiteboardQuery.isLoading) {
    return <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>Loading whiteboard…</section>;
  }

  if (!whiteboard) {
    return <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>Whiteboard not found.</section>;
  }

  return <WhiteboardEditorWorkspace key={whiteboard.id} whiteboard={whiteboard} />;
}
