"use client";

import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import { createShare, listShareAccessLog, listShareComments, listShares, revokeShare } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import type { ShareLink, SharePermissionLevel } from "@/lib/collab/types";

const focusableSelector =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

type ShareDialogProps = {
  taskId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

function formatExpiry(value?: string | null) {
  if (!value) {
    return "Never expires";
  }

  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
  }).format(new Date(value));
}

function absoluteUrl(path: string) {
  return typeof window === "undefined" ? path : `${window.location.origin}${path}`;
}

export function ShareDialog({ taskId, open, onOpenChange }: ShareDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const queryClient = useQueryClient();
  const sharesKey = collabKeys.shares(taskId);
  const [expiresInDays, setExpiresInDays] = useState("7");
  const [password, setPassword] = useState("");
  const [permissionLevel, setPermissionLevel] = useState<SharePermissionLevel>("View");
  const [created, setCreated] = useState<ShareLink | null>(null);
  const [copied, setCopied] = useState(false);
  const [detailsShareId, setDetailsShareId] = useState<string | null>(null);
  const sharesQuery = useQuery({
    queryKey: sharesKey,
    queryFn: () => listShares(taskId),
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
    mutationFn: (days?: number) => createShare(taskId, days, password, permissionLevel),
    onSuccess: (share) => {
      // The token is only returned on creation; later reads redact it.
      setCreated(share);
      setCopied(false);
      setPassword("");
      void queryClient.invalidateQueries({ queryKey: sharesKey });
    },
  });

  const revokeMutation = useMutation({
    mutationFn: revokeShare,
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
        aria-labelledby="share-dialog-title"
        tabIndex={-1}
        className="absolute left-1/2 top-1/2 w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-border bg-card p-5 shadow-2xl outline-none pv-animate-modal-centered"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 id="share-dialog-title" className="text-lg font-semibold">
              Share task publicly
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              Public links are scoped to this task and never allow editing.
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
            Access level
            <select
              value={permissionLevel}
              className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onChange={(event) => setPermissionLevel(event.currentTarget.value as SharePermissionLevel)}
            >
              <option value="View">View only</option>
              <option value="Comment">View + comment</option>
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
                    {" · "}
                    {share.permissionLevel === "Comment" ? "View + comment" : "View only"}
                  </p>
                  <div className="mt-3 flex flex-wrap justify-end gap-2">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => setDetailsShareId((current) => (current === share.id ? null : share.id))}
                    >
                      {detailsShareId === share.id ? "Hide activity" : "View activity"}
                    </Button>
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
                  {detailsShareId === share.id ? <ShareLinkDetails shareId={share.id} /> : null}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}

const logTimestampFormatter = new Intl.DateTimeFormat("en", {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

/** Guest comments + access log for one share link, fetched lazily when the owner expands it. */
function ShareLinkDetails({ shareId }: { shareId: string }) {
  const commentsQuery = useQuery({
    queryKey: collabKeys.shareComments(shareId),
    queryFn: () => listShareComments(shareId),
  });
  const accessLogQuery = useQuery({
    queryKey: collabKeys.shareAccessLog(shareId),
    queryFn: () => listShareAccessLog(shareId),
  });

  return (
    <div className="mt-3 grid gap-3 border-t border-border pt-3 text-xs">
      <div>
        <h5 className="font-semibold text-muted-foreground">Guest comments</h5>
        {commentsQuery.isLoading ? (
          <p className="mt-1 text-muted-foreground">Loading…</p>
        ) : (commentsQuery.data ?? []).length === 0 ? (
          <p className="mt-1 text-muted-foreground">No guest comments yet.</p>
        ) : (
          <ul className="mt-1 space-y-1">
            {commentsQuery.data!.map((comment) => (
              <li key={comment.id} className="rounded-lg bg-muted px-2 py-1">
                <span className="font-medium">{comment.guestName || "Anonymous"}:</span> {comment.body}
              </li>
            ))}
          </ul>
        )}
      </div>
      <div>
        <h5 className="font-semibold text-muted-foreground">Access log</h5>
        {accessLogQuery.isLoading ? (
          <p className="mt-1 text-muted-foreground">Loading…</p>
        ) : (accessLogQuery.data ?? []).length === 0 ? (
          <p className="mt-1 text-muted-foreground">No access attempts recorded yet.</p>
        ) : (
          <ul className="mt-1 space-y-1">
            {accessLogQuery.data!.map((entry) => (
              <li key={entry.id} className="rounded-lg bg-muted px-2 py-1">
                {entry.action} · {logTimestampFormatter.format(new Date(entry.createdAtUtc))}
                {entry.ipAddress ? ` · ${entry.ipAddress}` : ""}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
