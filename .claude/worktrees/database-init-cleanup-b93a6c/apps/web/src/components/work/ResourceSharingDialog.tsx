"use client";

import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import {
  grantResourcePermission,
  listResourcePermissions,
  permissionLevels,
  revokeResourcePermission,
  type PermissionLevel,
  type PrincipalType,
  type ResourceType,
} from "@/lib/work/sharing";

const focusableSelector =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

type ResourceSharingDialogProps = {
  resourceType: ResourceType;
  resourceId: string;
  resourceName: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * ADR-0003 minimal ACL UI: list/grant/revoke resource_permissions entries for a Space, Folder,
 * List or Task. principalId is a plain UUID field — there is no user/team picker yet (out of scope for
 * this change); copy a user id from the members list or a team id from the teams page.
 */
export function ResourceSharingDialog({
  resourceType,
  resourceId,
  resourceName,
  open,
  onOpenChange,
}: ResourceSharingDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const queryClient = useQueryClient();
  const queryKey = ["work", "resource-permissions", resourceType, resourceId];

  const [principalType, setPrincipalType] = useState<PrincipalType>("user");
  const [principalId, setPrincipalId] = useState("");
  const [level, setLevel] = useState<PermissionLevel>("view");

  const permissionsQuery = useQuery({
    queryKey,
    queryFn: () => listResourcePermissions(resourceType, resourceId),
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

      const focusable = Array.from(
        dialogRef.current?.querySelectorAll<HTMLElement>(focusableSelector) ?? [],
      );
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
    mutationFn: () => grantResourcePermission(resourceType, resourceId, principalType, principalId.trim(), level),
    onSuccess: () => {
      setPrincipalId("");
      void queryClient.invalidateQueries({ queryKey });
    },
  });

  const revokeMutation = useMutation({
    mutationFn: (grant: { principalType: string; principalId: string }) =>
      revokeResourcePermission(resourceType, resourceId, grant.principalType as PrincipalType, grant.principalId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey }),
  });

  if (!open) {
    return null;
  }

  const grants = permissionsQuery.data ?? [];

  return (
    <div className="fixed inset-0 z-[60]" role="presentation">
      <button
        type="button"
        aria-label="Close sharing dialog"
        className="absolute inset-0 cursor-default bg-slate-950/50 backdrop-blur-[1px]"
        onClick={() => onOpenChange(false)}
      />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="resource-sharing-title"
        tabIndex={-1}
        className="absolute left-1/2 top-1/2 w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-border bg-card p-5 shadow-2xl outline-none"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 id="resource-sharing-title" className="text-lg font-semibold">
              Share &ldquo;{resourceName}&rdquo;
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              Grant a user, team, or role explicit access to this {resourceType}.
            </p>
          </div>
          <Button ref={closeButtonRef} type="button" variant="ghost" size="sm" onClick={() => onOpenChange(false)}>
            Close
          </Button>
        </div>

        <form
          className="mt-5 grid grid-cols-[1fr_2fr_1fr_auto] items-end gap-2 rounded-xl border border-border p-3"
          onSubmit={(event) => {
            event.preventDefault();
            if (principalId.trim().length > 0) {
              grantMutation.mutate();
            }
          }}
        >
          <label className="grid gap-1 text-xs font-medium">
            Principal
            <select
              value={principalType}
              className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
              onChange={(event) => setPrincipalType(event.currentTarget.value as PrincipalType)}
            >
              <option value="user">User</option>
              <option value="team">Team</option>
              <option value="role">Role</option>
            </select>
          </label>
          <label className="grid gap-1 text-xs font-medium">
            {principalType === "user" ? "User id" : principalType === "team" ? "Team id" : "Role id"}
            <input
              value={principalId}
              placeholder="00000000-0000-0000-0000-000000000000"
              className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
              onChange={(event) => setPrincipalId(event.currentTarget.value)}
            />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Level
            <select
              value={level}
              className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
              onChange={(event) => setLevel(event.currentTarget.value as PermissionLevel)}
            >
              {permissionLevels.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>
          <Button type="submit" size="sm" disabled={grantMutation.isPending || principalId.trim().length === 0}>
            Grant
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
              No explicit grants yet.
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
                    <span className="text-muted-foreground">{grant.principalId}</span>
                    {" — "}
                    <span className="font-medium">{grant.level}</span>
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
