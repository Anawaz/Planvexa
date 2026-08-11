export const textInputClassName =
  "h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

export const textareaClassName =
  "min-h-28 rounded-lg border border-border bg-background px-3 py-2 text-sm leading-6 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

export const panelClassName = "rounded-[var(--radius)] border border-border bg-card shadow-sm";

export const numberFormatter = new Intl.NumberFormat("en");

export function formatIsoDateTime(value?: string | null) {
  if (!value) {
    return "Never";
  }

  return new Intl.DateTimeFormat("en", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

export function formatIsoDate(value?: string | null) {
  if (!value) {
    return "Never";
  }

  return new Intl.DateTimeFormat("en", {
    dateStyle: "medium",
  }).format(new Date(value));
}

export function copyToClipboard(value: string) {
  if (typeof navigator === "undefined" || !navigator.clipboard) {
    return Promise.resolve();
  }

  return navigator.clipboard.writeText(value);
}
