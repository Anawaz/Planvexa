"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import {
  createDocumentTemplate,
  exportDocumentMarkdown,
  getDocument,
  getDocumentVersions,
  revertDocument,
  updateDocument,
} from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import type { Document } from "@/lib/collab/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useMemberDirectory } from "@/lib/members";
import { useRecordRecentView } from "@/lib/recent/useRecordRecentView";
import { cn } from "@/lib/utils";
import { PlanvexaEditor } from "./lexical/PlanvexaEditor";
import { useDocumentAutosave } from "./lexical/useDocumentAutosave";
import {
  formatIsoDateTime,
  panelClassName,
  textInputClassName,
} from "./collab-ui";

/** Mirrors DocumentsAuthorizer.CanEdit + the private-document owner/admin rule server-side — used only
 * for UI affordances (disabling the toolbar). The real enforcement is server-side, both on every REST
 * write and in the collaboration room (Hocuspocus sets connectionConfig.readOnly from the .NET
 * can-collaborate check regardless of what this client believes). */
function canEditDocument(document: Document, role: string | undefined, currentUserId: string | undefined) {
  if (!role || role === "Guest") return false;
  if (!document.isPrivate) return true;
  return document.ownerUserId === currentUserId || role === "Admin" || role === "Owner";
}

function downloadMarkdown(filename: string, markdown: string) {
  const blob = new Blob([markdown], { type: "text/markdown" });
  const url = URL.createObjectURL(blob);
  const anchor = window.document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}

function DocumentEditorWorkspace({ document }: { document: Document }) {
  const queryClient = useQueryClient();
  const { workspaceId = "", currentWorkspace, currentUser, currentUserId } = useAppContext();
  const directory = useMemberDirectory();
  const [title, setTitle] = useState(document.title);
  const [isPrivate, setIsPrivate] = useState(document.isPrivate);
  const [statusMessage, setStatusMessage] = useState("");
  const [templateName, setTemplateName] = useState("");
  const versionsQuery = useQuery({
    queryKey: collabKeys.documentVersions(workspaceId, document.id),
    queryFn: () => getDocumentVersions(document.id),
  });
  const canEdit = canEditDocument(document, currentWorkspace?.role, currentUserId);
  const autosaveOnChange = useDocumentAutosave(document.id, canEdit);

  const saveMetaMutation = useMutation({
    mutationFn: () => updateDocument(document.id, { title, isPrivate }),
    onSuccess: (updated) => {
      setStatusMessage("Saved.");
      queryClient.setQueryData(collabKeys.document(workspaceId, document.id), updated);
      void queryClient.invalidateQueries({ queryKey: collabKeys.documents(workspaceId) });
    },
  });
  const revertMutation = useMutation({
    mutationFn: (versionId: string) => revertDocument(document.id, versionId),
    onSuccess: (updated) => {
      setStatusMessage("Reverted and saved as a new version. Reload to see the restored content in the editor.");
      setTitle(updated.title);
      setIsPrivate(updated.isPrivate);
      queryClient.setQueryData(collabKeys.document(workspaceId, document.id), updated);
      void queryClient.invalidateQueries({ queryKey: collabKeys.documents(workspaceId) });
      void queryClient.invalidateQueries({ queryKey: collabKeys.documentVersions(workspaceId, document.id) });
    },
  });
  const templateMutation = useMutation({
    mutationFn: () => createDocumentTemplate(document.id, templateName.trim() || document.title),
    onSuccess: () => {
      setStatusMessage("Saved as a reusable template.");
      setTemplateName("");
    },
  });
  const mutationError = saveMetaMutation.error ?? revertMutation.error ?? templateMutation.error;

  function submitMeta(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!title.trim()) {
      setStatusMessage("Title is required.");
      return;
    }

    saveMetaMutation.mutate();
  }

  async function exportMarkdown() {
    const markdown = await exportDocumentMarkdown(document.id);
    downloadMarkdown(`${document.title.replace(/[^a-z0-9-_]+/gi, "-")}.md`, markdown);
  }

  return (
    <section aria-labelledby="document-editor-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Documents · Wiki</p>
          <h1 id="document-editor-title" className="mt-2 text-3xl font-semibold tracking-tight">
            {document.title}
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Realtime collaborative editor. Autosaves periodically and every edit is visible live to anyone
            else with this document open.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" size="sm" onClick={() => void exportMarkdown()}>
            Export Markdown
          </Button>
          <Link href="/app/documents" className={buttonStyles({ variant: "outline", size: "sm" })}>
            Back to documents
          </Link>
        </div>
      </div>

      {mutationError ? (
        <p role="alert" className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300">
          {(mutationError as Error).message}
        </p>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
        <div className={cn(panelClassName, "overflow-hidden")}>
          <PlanvexaEditor
            documentId={document.id}
            workspaceId={workspaceId}
            initialContent={document.content}
            userLabel={currentUser?.displayName ?? "Anonymous"}
            canEdit={canEdit}
            onChange={autosaveOnChange}
          />
        </div>

        <aside className="space-y-4">
          <form onSubmit={submitMeta} className={cn(panelClassName, "p-4")}>
            <h2 className="text-sm font-semibold">Document settings</h2>
            <div className="mt-3 grid gap-3">
              <label className="grid gap-1 text-xs font-medium">
                Title
                <input value={title} onChange={(event) => setTitle(event.target.value)} className={textInputClassName} />
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={isPrivate}
                  onChange={(event) => setIsPrivate(event.target.checked)}
                  className="size-4 rounded border-border accent-[var(--primary)]"
                />
                {isPrivate ? "Private to owner" : "Shared with workspace"}
              </label>
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
            <p className="mt-1 text-sm font-semibold">{formatIsoDateTime(document.updatedAtUtc)}</p>
            <p className="mt-4 text-xs font-medium uppercase tracking-wide text-muted-foreground">Save as template</p>
            <div className="mt-2 flex gap-2">
              <input
                value={templateName}
                onChange={(event) => setTemplateName(event.target.value)}
                placeholder={document.title}
                className={cn(textInputClassName, "h-8 flex-1 text-xs")}
              />
              <Button type="button" size="sm" variant="outline" disabled={templateMutation.isPending} onClick={() => templateMutation.mutate()}>
                Save
              </Button>
            </div>
          </div>

          <details className="rounded-xl border border-border bg-background p-3" open>
            <summary className="cursor-pointer text-sm font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
              Version history
            </summary>
            <div className="mt-3 space-y-3">
              {versionsQuery.isLoading ? (
                <p className="text-sm text-muted-foreground">Loading versions…</p>
              ) : (
                (versionsQuery.data ?? []).map((version) => (
                  <article key={version.id} className="rounded-lg border border-border bg-card p-3">
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <h2 className="text-sm font-semibold">{formatIsoDateTime(version.createdAtUtc)}</h2>
                        <p className="mt-1 text-xs text-muted-foreground">By {directory.getLabel(version.authorUserId)}</p>
                      </div>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        disabled={revertMutation.isPending}
                        onClick={() => revertMutation.mutate(version.id)}
                      >
                        Revert
                      </Button>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{version.contentPreview}</p>
                  </article>
                ))
              )}
            </div>
          </details>
        </aside>
      </div>
    </section>
  );
}

export function DocumentEditorPageClient({ documentId }: { documentId: string }) {
  useRecordRecentView("document", documentId);
  const { workspaceId = "" } = useAppContext();
  const documentQuery = useQuery({
    queryKey: collabKeys.document(workspaceId, documentId),
    queryFn: () => getDocument(documentId),
  });
  const document = documentQuery.data;

  if (documentQuery.isLoading) {
    return (
      <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>
        Loading document…
      </section>
    );
  }

  if (!document) {
    return (
      <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>
        Document not found.
      </section>
    );
  }

  return <DocumentEditorWorkspace key={document.id} document={document} />;
}
