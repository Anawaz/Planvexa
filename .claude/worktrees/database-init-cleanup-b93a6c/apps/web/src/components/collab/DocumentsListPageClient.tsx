"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { createDocument, deleteDocument, listDocumentTemplates, listDocuments } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import type { DocumentSummary } from "@/lib/collab/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { cn } from "@/lib/utils";
import { formatIsoDateTime, panelClassName, textInputClassName } from "./collab-ui";

function documentScope(document: DocumentSummary) {
  if (document.listId) {
    return { href: `/app/lists/${document.listId}`, label: "List" };
  }

  if (document.spaceId) {
    return { href: "/app/spaces", label: "Space" };
  }

  return { href: "/app/documents", label: "Workspace" };
}

/** Depth in the wiki tree, walking parentDocumentId — bounded by the list length so a data anomaly can
 * never spin this into an infinite loop (same defensive bound as the server's DocumentHierarchy walk). */
function documentDepth(document: DocumentSummary, byId: Map<string, DocumentSummary>): number {
  let depth = 0;
  let current: DocumentSummary | undefined = document;
  const seen = new Set<string>();
  while (current?.parentDocumentId && !seen.has(current.id)) {
    seen.add(current.id);
    current = byId.get(current.parentDocumentId);
    depth += 1;
    if (depth > byId.size) break;
  }

  return depth;
}

export function DocumentsListPageClient() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [title, setTitle] = useState("");
  const [isPrivate, setIsPrivate] = useState(false);
  const [parentDocumentId, setParentDocumentId] = useState("");
  const [templateId, setTemplateId] = useState("");
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);
  const documentsQuery = useQuery({
    queryKey: collabKeys.documents(workspaceId),
    queryFn: listDocuments,
  });
  const templatesQuery = useQuery({
    queryKey: [...collabKeys.documentsRoot(workspaceId), "templates"],
    queryFn: listDocumentTemplates,
  });
  const createMutation = useMutation({
    mutationFn: createDocument,
    onSuccess: (document) => {
      setTitle("");
      setIsPrivate(false);
      setParentDocumentId("");
      setTemplateId("");
      void queryClient.invalidateQueries({ queryKey: collabKeys.documentsRoot(workspaceId) });
      router.push(`/app/documents/${document.id}`);
    },
  });
  const deleteMutation = useMutation({
    mutationFn: deleteDocument,
    onSuccess: () => {
      setPendingDeleteId(null);
      void queryClient.invalidateQueries({ queryKey: collabKeys.documentsRoot(workspaceId) });
    },
  });
  const mutationError = createMutation.error ?? deleteMutation.error;

  function submitDocument(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    // ponytail: workspace-level documents only; attach to a space/list/task from those surfaces.
    createMutation.mutate({
      title: title.trim() || "Untitled document",
      content: "",
      isPrivate,
      parentDocumentId: parentDocumentId || null,
      templateId: templateId || null,
    });
  }

  const documents = documentsQuery.data ?? [];
  const documentsById = new Map(documents.map((d) => [d.id, d]));

  return (
    <section aria-labelledby="documents-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Documents</p>
          <h1 id="documents-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Documents
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Workspace documents with server-side version history.
          </p>
        </div>
        <a href="#new-document" className={buttonStyles({ variant: "primary", size: "sm" })}>
          New document
        </a>
      </div>

      {mutationError ? (
        <p
          role="alert"
          className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          This document change could not be saved: {(mutationError as Error).message}
        </p>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
        <section className={cn(panelClassName, "overflow-hidden")} aria-labelledby="document-list-title">
          <header className="border-b border-border p-4">
            <h2 id="document-list-title" className="text-sm font-semibold">
              Document library
            </h2>
            <p className="mt-1 text-xs text-muted-foreground">
              Scope links point to the existing workspace/list surfaces.
            </p>
          </header>
          <div className="overflow-x-auto">
            <table className="min-w-full text-left text-sm">
              <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-4 py-3 font-semibold">Title</th>
                  <th className="px-4 py-3 font-semibold">Scope</th>
                  <th className="px-4 py-3 font-semibold">Visibility</th>
                  <th className="px-4 py-3 font-semibold">Updated</th>
                  <th className="px-4 py-3 text-right font-semibold">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {documents.map((document) => {
                  const scope = documentScope(document);

                  return (
                    <tr key={document.id}>
                      <td className="px-4 py-3">
                        <span style={{ paddingLeft: `${documentDepth(document, documentsById) * 1.25}rem` }} className="inline-flex items-center gap-1">
                          {document.parentDocumentId ? <span className="text-muted-foreground">└</span> : null}
                          <Link
                            href={`/app/documents/${document.id}`}
                            className="font-semibold text-foreground underline-offset-4 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                          >
                            {document.title}
                          </Link>
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        <Link
                          href={scope.href}
                          className="text-primary underline-offset-4 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                        >
                          {scope.label}
                        </Link>
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={cn(
                            "rounded-full px-2.5 py-1 text-xs font-semibold",
                            document.isPrivate
                              ? "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200"
                              : "bg-primary/10 text-primary",
                          )}
                        >
                          {document.isPrivate ? "Private" : "Shared"}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-muted-foreground">
                        {formatIsoDateTime(document.updatedAtUtc)}
                      </td>
                      <td className="px-4 py-3 text-right">
                        {pendingDeleteId === document.id ? (
                          <span className="inline-flex flex-wrap items-center justify-end gap-2">
                            <span className="text-xs text-muted-foreground">Delete for everyone?</span>
                            <Button
                              type="button"
                              size="sm"
                              variant="outline"
                              className="border-red-300 text-red-700 dark:border-red-900 dark:text-red-400"
                              disabled={deleteMutation.isPending}
                              onClick={() => deleteMutation.mutate(document.id)}
                            >
                              Confirm
                            </Button>
                            <Button
                              type="button"
                              size="sm"
                              variant="ghost"
                              onClick={() => setPendingDeleteId(null)}
                            >
                              Cancel
                            </Button>
                          </span>
                        ) : (
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            className="text-red-600 hover:text-red-700 dark:text-red-400"
                            aria-label={`Delete ${document.title}`}
                            onClick={() => setPendingDeleteId(document.id)}
                          >
                            Delete
                          </Button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
            {documentsQuery.isLoading ? (
              <p className="p-4 text-sm text-muted-foreground">Loading documents…</p>
            ) : documents.length === 0 ? (
              <EmptyState
                className="m-4"
                title="No documents yet"
                description="Use the “New document” form beside this table to write your first spec, note or runbook."
              />
            ) : null}
          </div>
        </section>

        <form
          id="new-document"
          onSubmit={submitDocument}
          className={cn(panelClassName, "p-4")}
          aria-labelledby="new-document-title"
        >
          <h2 id="new-document-title" className="text-sm font-semibold">
            Create document
          </h2>
          <div className="mt-4 grid gap-3">
            <label className="grid gap-1 text-xs font-medium">
              Title
              <input
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                className={textInputClassName}
                placeholder="Launch retrospective"
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
            <label className="grid gap-1 text-xs font-medium">
              Parent document (wiki nesting)
              <select
                value={parentDocumentId}
                onChange={(event) => setParentDocumentId(event.target.value)}
                className={textInputClassName}
              >
                <option value="">No parent (top level)</option>
                {documents.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.title}
                  </option>
                ))}
              </select>
            </label>
            <label className="grid gap-1 text-xs font-medium">
              Start from template
              <select
                value={templateId}
                onChange={(event) => setTemplateId(event.target.value)}
                className={textInputClassName}
              >
                <option value="">Blank document</option>
                {(templatesQuery.data ?? []).map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
            </label>
            <Button type="submit" size="sm" disabled={createMutation.isPending}>
              New document
            </Button>
          </div>
        </form>
      </div>
    </section>
  );
}
