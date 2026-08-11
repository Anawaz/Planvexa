import { getFormatPreferences } from "@/lib/i18n/formatPreferences";

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

// Date-only values (sprint/holiday/leave dates) are stored as UTC midnight (see dateInputToUtc)
// and have no meaningful time-of-day, so they must render in UTC too - otherwise a viewer west
// of UTC sees the calendar day shifted back by one. `locale` is optional: it defaults to the
// signed-in user's saved preference (AppContext -> setFormatPreferences), falling back to the
// browser's default locale when neither is set.
export function formatShortDate(value: Date | string, locale?: string) {
  return new Intl.DateTimeFormat(locale ?? getFormatPreferences().locale, {
    month: "short",
    day: "numeric",
    timeZone: "UTC",
  }).format(new Date(value));
}

export function formatLongDate(value: Date | string, locale?: string) {
  return new Intl.DateTimeFormat(locale ?? getFormatPreferences().locale, {
    month: "long",
    day: "numeric",
    year: "numeric",
    timeZone: "UTC",
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

/**
 * Start of the UTC week containing `date`. `weekStartsOn` follows the time-policy convention
 * (0 = Sunday .. 6 = Saturday, matching Date#getUTCDay()); defaults to Monday for callers with no
 * workspace policy to honour (Workload/Team's arbitrary 14-day ranges).
 */
export function startOfUtcWeek(date: Date, weekStartsOn = 1) {
  const next = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
  const diff = (next.getUTCDay() - weekStartsOn + 7) % 7;
  next.setUTCDate(next.getUTCDate() - diff);
  return next;
}
