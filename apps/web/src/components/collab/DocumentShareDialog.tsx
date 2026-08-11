"use client";

import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import { createDocumentShare, listDocumentShares, revokeDocumentShare } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import type { DocumentShareLink } from "@/lib/collab/types";
import { useAppContext } from "@/lib/app-context/AppContext";

const focusableSelector =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

type DocumentShareDialogProps = {
  documentId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

function formatExpiry(value?: string | null) {
  if (!value) {
    return "Never expires";
  }

  return new Intl.DateTimeFormat("en", { month: "short", day: "numeric", year: "numeric" }).format(new Date(value));
}

function absoluteUrl(path: string) {
  return typeof window === "undefined" ? path : `${window.location.origin}${path}`;
}

/**
 * Public, view-only share link dialog for documents — same expiration/password/revocation UX as
 * ShareDialog (tasks), minus the permission-level picker and guest-comment/access-log panels, since
 * public document sharing is view-only and has no anonymous-comment feature (see
 * DocumentShareLinkService's doc comment).
 */
export function DocumentShareDialog({ documentId, open, onOpenChange }: DocumentShareDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const sharesKey = collabKeys.documentShares(workspaceId, documentId);
  const [expiresInDays, setExpiresInDays] = useState("7");
  const [password, setPassword] = useState("");
  const [created, setCreated] = useState<DocumentShareLink | null>(null);
  const [copied, setCopied] = useState(false);
  const sharesQuery = useQuery({
    queryKey: sharesKey,
    queryFn: () => listDocumentShares(documentId),
    enabled: open,
  });

  useEffect(() => {
    if (!open) {
      return;
    }

    const previousFocus = document.activeElement as HTMLElement | null;
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

      const focusable = Array.from(
        dialogRef.current?.querySelectorAll<HTMLElement>(focusableSelector) ?? [],
      ).filter((element) => !element.hasAttribute("disabled"));

      if (focusable.length === 0) {
        event.preventDefault();
        dialogRef.current?.focus();
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

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      previousFocus?.focus();
    };
  }, [onOpenChange, open]);

  const createMutation = useMutation({
    mutationFn: (days?: number) => createDocumentShare(documentId, days, password),
    onSuccess: (share) => {
      // The token is only returned on creation; later reads redact it.
      setCreated(share);
      setCopied(false);
      setPassword("");
      void queryClient.invalidateQueries({ queryKey: sharesKey });
    },
  });

  const revokeMutation = useMutation({
    mutationFn: revokeDocumentShare,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: sharesKey });
    },
  });

  if (!open) {
    return null;
  }

  const shares = sharesQuery.data ?? [];

  async function copyCreatedLink() {
    if (!created) {
      return;
    }

    await navigator.clipboard?.writeText(absoluteUrl(created.url));
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1600);
  }

  function createNewShare() {
    const days = expiresInDays === "never" ? undefined : Number(expiresInDays);
    createMutation.mutate(days);
  }

  return (
    <div className="fixed inset-0 z-[60]" role="presentation">
      <button
        type="button"
        aria-label="Close share dialog"
        className="absolute inset-0 cursor-default bg-slate-950/50 backdrop-blur-[1px] pv-animate-backdrop"
        onClick={() => onOpenChange(false)}
      />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="document-share-dialog-title"
        tabIndex={-1}
        className="absolute left-1/2 top-1/2 w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-border bg-card p-5 shadow-2xl outline-none pv-animate-modal-centered"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 id="document-share-dialog-title" className="text-lg font-semibold">
              Share document publicly
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              Public links are read-only and never allow editing or commenting.
            </p>
          </div>
          <Button ref={closeButtonRef} type="button" variant="ghost" size="sm" onClick={() => onOpenChange(false)}>
            Close
          </Button>
        </div>

        <div className="mt-5 grid gap-3 rounded-xl border border-border p-3">
          <label className="grid gap-2 text-sm font-medium">
            Expiration
            <select
              value={expiresInDays}
              className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onChange={(event) => setExpiresInDays(event.currentTarget.value)}
            >
              <option value="7">7 days</option>
              <option value="30">30 days</option>
              <option value="never">Never</option>
            </select>
          </label>
          <label className="grid gap-2 text-sm font-medium">
            Password (optional)
            <input
              type="password"
              value={password}
              placeholder="Leave blank for no password"
              className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onChange={(event) => setPassword(event.currentTarget.value)}
            />
          </label>
          <Button type="button" disabled={createMutation.isPending} onClick={createNewShare}>
            {createMutation.isPending ? "Creating…" : "Create share link"}
          </Button>
        </div>

        {created ? (
          <div role="alert" className="mt-4 rounded-xl border border-primary bg-primary/10 p-3 text-sm">
            <p className="font-semibold text-primary">Copy this link now — the token is shown once.</p>
            <code className="mt-2 block break-all rounded-lg bg-background px-3 py-2 text-foreground">
              {absoluteUrl(created.url)}
            </code>
            <div className="mt-3 flex justify-end gap-2">
              <Button type="button" variant="secondary" size="sm" onClick={() => void copyCreatedLink()}>
                {copied ? "Copied" : "Copy link"}
              </Button>
              <Button type="button" variant="ghost" size="sm" onClick={() => setCreated(null)}>
                Dismiss
              </Button>
            </div>
          </div>
        ) : null}

        <div className="mt-5 space-y-3">
          <h4 className="text-sm font-semibold">Active links</h4>
          {sharesQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">Loading share links…</p>
          ) : shares.length === 0 ? (
            <p className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
              No public links yet.
            </p>
          ) : (
            <ul className="space-y-2">
              {shares.map((share) => (
                <li key={share.id} className="rounded-xl border border-border bg-background p-3">
                  <p className="break-all text-sm font-medium">{share.url}</p>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {formatExpiry(share.expiresAtUtc)}
                    {share.requiresPassword ? " · Password protected" : ""}
                  </p>
                  <div className="mt-3 flex justify-end">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      disabled={revokeMutation.isPending}
                      onClick={() => revokeMutation.mutate(share.id)}
                    >
                      Revoke
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}
