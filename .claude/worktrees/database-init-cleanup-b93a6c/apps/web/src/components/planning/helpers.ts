export const hourFormatter = new Intl.NumberFormat("en", {
  maximumFractionDigits: 1,
  minimumFractionDigits: 0,
});

export const numberFormatter = new Intl.NumberFormat("en");

export const percentFormatter = new Intl.NumberFormat("en", {
  maximumFractionDigits: 0,
  style: "percent",
});

export function formatHours(value: number) {
  return `${hourFormatter.format(value)}h`;
}

export function dayKey(date: Date | string) {
  return new Date(date).toISOString().slice(0, 10);
}

export function toIsoDateInput(value: Date | string) {
  return dayKey(value);
}

export function dateInputToUtc(value: string) {
  return new Date(`${value}T00:00:00.000Z`).toISOString();
}

/** Shifts an ISO date string by whole days (UTC), preserving null/undefined. */
export function shiftDateIso(iso: string | null | undefined, deltaDays: number): string | null {
  if (!iso) {
    return null;
  }

  const date = new Date(iso);
  date.setUTCDate(date.getUTCDate() + deltaDays);
  return date.toISOString();
}

export function formatShortDate(value: Date | string) {
  return new Intl.DateTimeFormat("en", { month: "short", day: "numeric" }).format(
    new Date(value),
  );
}

export function formatLongDate(value: Date | string) {
  return new Intl.DateTimeFormat("en", {
    month: "long",
    day: "numeric",
    year: "numeric",
  }).format(new Date(value));
}

export function addDays(date: Date, days: number) {
  const next = new Date(date);
  next.setUTCDate(next.getUTCDate() + days);
  return next;
}

export function addMonths(date: Date, months: number) {
  const next = new Date(date);
  next.setUTCMonth(next.getUTCMonth() + months);
  return next;
}

export function startOfUtcMonth(date: Date) {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1));
}

export function startOfUtcWeek(date: Date) {
  const next = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
  const diff = (next.getUTCDay() + 6) % 7;
  next.setUTCDate(next.getUTCDate() - diff);
  return next;
}
