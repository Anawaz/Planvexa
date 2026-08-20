"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/Button";
import { ApiError } from "@/lib/api-client";
import { cn } from "@/lib/utils";

export {
  formatIsoDate,
  formatIsoDateTime,
  panelClassName,
  selectClassName,
  tableHeaderClassName,
  textInputClassName,
  toUtcDateEnd,
  toUtcDateStart,
  IsoDateTime,
  PageHeader,
} from "@/components/admin/admin-ui";

/**
 * The host console's navigation. Kept here rather than in `app-shell/nav-config` because that file is
 * the WORKSPACE shell's navigation and every entry in it is workspace-scoped — mixing an
 * instance-level section into it would put host links in the workspace sidebar and the command
 * palette for people who cannot use them.
 */
type HostNavItem = { href: string; label: string; /** Match the path exactly — the console root would otherwise be "active" on every page. */ exact?: boolean };

export const hostNavItems: HostNavItem[] = [
  { href: "/host", label: "Overview", exact: true },
  { href: "/host/workspaces", label: "Workspaces" },
  { href: "/host/users", label: "Users" },
  { href: "/host/activity", label: "Activity" },
  { href: "/host/logs", label: "Logs" },
  { href: "/host/health", label: "Health" },
  { href: "/host/settings", label: "Settings" },
];

export function HostNav({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();

  return (
    <nav aria-label="Host administration" className="space-y-1">
      {hostNavItems.map((item) => {
        const active = item.exact ? pathname === item.href : pathname.startsWith(item.href);
        return (
          <Link
            key={item.href}
            href={item.href}
            onClick={onNavigate}
            aria-current={active ? "page" : undefined}
            className={cn(
              "block rounded-lg px-3 py-2 text-sm font-medium transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none",
              active ? "bg-primary/10 text-primary" : "text-muted-foreground hover:bg-muted hover:text-foreground",
            )}
          >
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}

const numberFormatter = new Intl.NumberFormat("en");

export function StatTile({ label, value, hint }: { label: string; value: number | string; hint?: ReactNode }) {
  return (
    <div className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
      <p className="text-sm text-muted-foreground">{label}</p>
      <p className="mt-1 text-2xl font-semibold tabular-nums">
        {typeof value === "number" ? numberFormatter.format(value) : value}
      </p>
      {hint ? <p className="mt-1 text-xs text-muted-foreground">{hint}</p> : null}
    </div>
  );
}

export function formatBytes(bytes: number) {
  if (bytes <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const exponent = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  const value = bytes / 1024 ** exponent;
  return `${value.toFixed(exponent === 0 ? 0 : 1)} ${units[exponent]}`;
}

/**
 * Renders a mutation's failure. The API's ProblemDetails `detail` carries the actual reason (the
 * last-host-admin guard, a slug that did not match), which is the only text worth showing — a bare
 * status code tells the operator nothing about what to do differently.
 */
export function MutationError({ error }: { error: unknown }) {
  if (!error) return null;
  const message = error instanceof ApiError ? error.message : "Something went wrong. Try again.";
  return (
    <p role="alert" className="rounded-lg bg-red-100 px-4 py-3 text-sm font-medium text-red-800 dark:bg-red-950 dark:text-red-200">
      {message}
    </p>
  );
}

/**
 * Destructive-action gate. Every irreversible host action goes through this, and the ones that cannot
 * be undone at all (deleting a workspace) additionally require `confirmText` to be retyped — the same
 * contract the Owner-facing delete already uses, so the muscle memory transfers.
 *
 * Not a portal/focus-trap dialog: it renders inline in the page flow, so there is nothing to trap
 * focus into or restore focus from, and no new dependency for one screen. `autoFocus` puts the
 * keyboard where the decision is made.
 */
export function ConfirmAction({
  title,
  description,
  actionLabel,
  confirmText,
  pending,
  error,
  onConfirm,
  onCancel,
}: {
  title: string;
  description: ReactNode;
  actionLabel: string;
  /** When set, the operator must retype this exact string before the action enables. */
  confirmText?: string;
  pending: boolean;
  error: unknown;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const [typed, setTyped] = useState("");
  const satisfied = !confirmText || typed === confirmText;

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (satisfied && !pending) onConfirm();
  }

  return (
    <form
      onSubmit={submit}
      className="space-y-4 rounded-[var(--radius)] border border-red-300 bg-red-50 p-4 dark:border-red-900 dark:bg-red-950/40"
    >
      <div>
        <p className="text-sm font-semibold text-red-900 dark:text-red-200">{title}</p>
        <div className="mt-1 text-sm text-red-800 dark:text-red-300">{description}</div>
      </div>

      {confirmText ? (
        <div className="grid gap-2">
          <label htmlFor="confirm-text" className="text-sm font-medium text-red-900 dark:text-red-200">
            Type <span className="font-mono font-semibold">{confirmText}</span> to confirm
          </label>
          <input
            id="confirm-text"
            autoFocus
            value={typed}
            onChange={(event) => setTyped(event.target.value)}
            autoComplete="off"
            className="h-10 rounded-lg border border-red-300 bg-background px-3 font-mono text-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring dark:border-red-900"
          />
        </div>
      ) : null}

      <MutationError error={error} />

      <div className="flex flex-wrap gap-3">
        <Button
          type="submit"
          size="sm"
          disabled={!satisfied || pending}
          className="bg-red-600 text-white hover:opacity-90"
        >
          {pending ? "Working…" : actionLabel}
        </Button>
        <Button type="button" size="sm" variant="outline" onClick={onCancel} disabled={pending}>
          Cancel
        </Button>
      </div>
    </form>
  );
}

/** Skip/take paging shared by the workspaces, users, activity and logs tables. */
export function Pager({
  skip,
  take,
  total,
  onChange,
}: {
  skip: number;
  take: number;
  total: number;
  onChange: (skip: number) => void;
}) {
  const from = total === 0 ? 0 : skip + 1;
  const to = Math.min(skip + take, total);

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3 text-sm text-muted-foreground">
      <span>
        {numberFormatter.format(from)}–{numberFormatter.format(to)} of {numberFormatter.format(total)}
      </span>
      <div className="flex gap-2">
        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={skip <= 0}
          onClick={() => onChange(Math.max(0, skip - take))}
        >
          Previous
        </Button>
        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={to >= total}
          onClick={() => onChange(skip + take)}
        >
          Next
        </Button>
      </div>
    </div>
  );
}
