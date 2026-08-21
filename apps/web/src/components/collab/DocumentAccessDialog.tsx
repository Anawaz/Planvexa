"use client";

import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import { ResourcePicker } from "@/components/ui/ResourcePicker";
import {
  grantDocumentPermission,
  listDocumentPermissions,
  revokeDocumentPermission,
} from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import type { DocumentPermissionLevel, DocumentPrincipalType } from "@/lib/collab/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useMemberDirectory, useTeams } from "@/lib/members";

const focusableSelector =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

type DocumentAccessDialogProps = {
  documentId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * ADR-0003 private sharing: grants/revokes View/Edit access to specific Users or Teams on a document
 * (relevant once it is private — a shared document is already visible to the whole workspace). Uses
 * ResourcePicker over the global search endpoint rather than a raw id field (spec: normal users must
 * never enter a raw UUID).
 */
export function DocumentAccessDialog({ documentId, open, onOpenChange }: DocumentAccessDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const directory = useMemberDirectory();
  const teamsQuery = useTeams(open);
  const permissionsKey = collabKeys.documentPermissions(workspaceId, documentId);

  const [principalType, setPrincipalType] = useState<DocumentPrincipalType>("user");
  const [principalId, setPrincipalId] = useState("");
  const [level, setLevel] = useState<DocumentPermissionLevel>("view");

  const permissionsQuery = useQuery({
    queryKey: permissionsKey,
    queryFn: () => listDocumentPermissions(documentId),
    enabled: open,
  });

  useEffect(() => {
    if (!open) {
      return;
    }

    window.requestAnimationFrame(() => closeButtonRef.current?.focus());

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.preventDefault();
        onOpenChange(false);
        return;
      }

      if (event.key !== "Tab") {
        return;
      }

      const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>(focusableSelector) ?? []);
      if (focusable.length === 0) {
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [onOpenChange, open]);

  const grantMutation = useMutation({
    mutationFn: () => grantDocumentPermission(documentId, principalType, principalId, level),
    onSuccess: () => {
      setPrincipalId("");
      void queryClient.invalidateQueries({ queryKey: permissionsKey });
    },
  });

  const revokeMutation = useMutation({
    mutationFn: (grant: { principalType: string; principalId: string }) =>
      revokeDocumentPermission(documentId, grant.principalType as DocumentPrincipalType, grant.principalId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: permissionsKey }),
  });

  if (!open) {
    return null;
  }

  const grants = permissionsQuery.data ?? [];

  function principalLabel(grantPrincipalType: string, grantPrincipalId: string) {
    if (grantPrincipalType === "team") {
      return teamsQuery.data?.find((t) => t.id === grantPrincipalId)?.name ?? grantPrincipalId;
    }

    return directory.getLabel(grantPrincipalId);
  }

  return (
    <div className="fixed inset-0 z-[60]" role="presentation">
      <button
        type="button"
        aria-label="Close access dialog"
        className="absolute inset-0 cursor-default bg-slate-950/50 backdrop-blur-[1px]"
        onClick={() => onOpenChange(false)}
      />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="document-access-dialog-title"
        tabIndex={-1}
        className="absolute left-1/2 top-1/2 w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-border bg-card max-h-[calc(100dvh-2rem)] overflow-y-auto p-4 sm:p-5 shadow-2xl outline-none"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 id="document-access-dialog-title" className="text-lg font-semibold">
              Manage access
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              Grant a specific person or team access to this document while it is private.
            </p>
          </div>
          <Button ref={closeButtonRef} type="button" variant="ghost" size="sm" onClick={() => onOpenChange(false)}>
            Close
          </Button>
        </div>

        <form
          className="mt-5 grid gap-2 rounded-xl border border-border p-3"
          onSubmit={(event) => {
            event.preventDefault();
            if (principalId.trim().length > 0) {
              grantMutation.mutate();
            }
          }}
        >
          <div className="grid grid-cols-2 gap-2">
            <label className="grid gap-1 text-xs font-medium">
              Share with
              <select
                value={principalType}
                className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
                onChange={(event) => {
                  setPrincipalType(event.currentTarget.value as DocumentPrincipalType);
                  setPrincipalId("");
                }}
              >
                <option value="user">Person</option>
                <option value="team">Team</option>
              </select>
            </label>
            <label className="grid gap-1 text-xs font-medium">
              Access level
              <select
                value={level}
                className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
                onChange={(event) => setLevel(event.currentTarget.value as DocumentPermissionLevel)}
              >
                <option value="view">Can view</option>
                <option value="edit">Can edit</option>
              </select>
            </label>
          </div>
          <label className="grid gap-1 text-xs font-medium">
            {principalType === "user" ? "Person" : "Team"}
            <ResourcePicker
              types={principalType === "user" ? ["Member"] : ["Team"]}
              value={principalId}
              onChange={(id) => setPrincipalId(id)}
              placeholder={principalType === "user" ? "Search people…" : "Search teams…"}
            />
          </label>
          <Button type="submit" size="sm" className="justify-self-end" disabled={grantMutation.isPending || principalId.trim().length === 0}>
            Grant access
          </Button>
        </form>

        {grantMutation.isError ? (
          <p role="alert" className="mt-2 text-sm text-red-600 dark:text-red-400">
            {grantMutation.error instanceof Error ? grantMutation.error.message : "Could not grant access."}
          </p>
        ) : null}

        <div className="mt-5 space-y-2">
          <h4 className="text-sm font-semibold">Current access</h4>
          {permissionsQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">Loading…</p>
          ) : grants.length === 0 ? (
            <p className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
              No one else has been granted access yet.
            </p>
          ) : (
            <ul className="space-y-2">
              {grants.map((grant) => (
                <li
                  key={grant.id}
                  className="flex items-center justify-between gap-3 rounded-xl border border-border bg-background p-3 text-sm"
                >
                  <span className="min-w-0 truncate">
                    <span className="font-medium capitalize">{grant.principalType}</span>{" "}
                    <span>{principalLabel(grant.principalType, grant.principalId)}</span>
                    {" — "}
                    <span className="font-medium capitalize">{grant.level}</span>
                  </span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={revokeMutation.isPending}
                    onClick={() => revokeMutation.mutate(grant)}
                  >
                    Revoke
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}
