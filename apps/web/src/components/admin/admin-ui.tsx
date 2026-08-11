import type { ReactNode } from "react";

export const panelClassName = "rounded-[var(--radius)] border border-border bg-card shadow-sm";
export const textInputClassName =
  "h-10 rounded-lg border border-border bg-background px-3 text-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50";
export const selectClassName =
  "h-10 rounded-lg border border-border bg-background px-3 text-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50";
export const tableHeaderClassName = "bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground";
export const numberFormatter = new Intl.NumberFormat("en");

export function formatMoney(amount: number, currency = "USD") {
  return new Intl.NumberFormat("en", {
    style: "currency",
    currency,
  }).format(amount);
}

export function formatIsoDate(value?: string | null) {
  if (!value) {
    return "Never";
  }

  return new Intl.DateTimeFormat("en", {
    dateStyle: "medium",
  }).format(new Date(value));
}

export function formatIsoDateTime(value?: string | null) {
  if (!value) {
    return "Never";
  }

  return new Intl.DateTimeFormat("en", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

export function toUtcDateStart(value: string) {
  return new Date(`${value}T00:00:00.000Z`).toISOString();
}

export function toUtcDateEnd(value: string) {
  return new Date(`${value}T23:59:59.999Z`).toISOString();
}

export function PageHeader({
  id,
  eyebrow,
  title,
  description,
}: {
  id?: string;
  eyebrow: string;
  title: string;
  description: ReactNode;
}) {
  return (
    <div>
      <p className="text-sm font-medium text-primary">{eyebrow}</p>
      <h1 id={id} className="mt-2 text-3xl font-semibold tracking-tight">{title}</h1>
      <p className="mt-3 max-w-3xl text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

export function IsoDateTime({
  value,
  dateOnly = false,
  fallback = "Never",
}: {
  value?: string | null;
  dateOnly?: boolean;
  fallback?: string;
}) {
  if (!value) {
    return <span>{fallback}</span>;
  }

  return <time dateTime={value}>{dateOnly ? formatIsoDate(value) : formatIsoDateTime(value)}</time>;
}
