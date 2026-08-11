export function formatDuration(seconds: number) {
  const safeSeconds = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(safeSeconds / 3600);
  const minutes = Math.floor((safeSeconds % 3600) / 60);

  return `${hours}:${minutes.toString().padStart(2, "0")}`;
}

export function formatDecimalHours(seconds: number) {
  return `${(seconds / 3600).toFixed(2)}h`;
}

export const moneyFormatter = new Intl.NumberFormat("en", {
  style: "currency",
  currency: "USD",
  maximumFractionDigits: 0,
});

export function toDateInputValue(date: Date) {
  return date.toISOString().slice(0, 10);
}

export function toLocalDateTimeInputValue(date: Date) {
  const offsetMs = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offsetMs).toISOString().slice(0, 16);
}

export function fromLocalDateTimeInputValue(value: string) {
  return new Date(value).toISOString();
}
