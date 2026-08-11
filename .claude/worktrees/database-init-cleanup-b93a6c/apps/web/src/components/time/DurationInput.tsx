"use client";

import { useId } from "react";
import { formatDurationText, parseDuration } from "@/lib/time/duration";
import { cn } from "@/lib/utils";

const PRESETS = ["15m", "30m", "45m", "1h", "1h 30m", "2h", "4h", "8h"];

const errorMessages = {
  invalid: "Try 1h 45m 44s, 90m or 1:45. A plain number means minutes.",
  "too-long": "Keep a single entry under 24h.",
} as const;

const chipClassName =
  "rounded-full border border-border bg-background px-2.5 py-1 text-xs font-medium text-muted-foreground transition-colors [@media(hover:hover)]:hover:bg-muted [@media(hover:hover)]:hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

/**
 * Free-text duration box: `1h 45m 44s`, `90m`, `2:30` — with live feedback and
 * quick-pick chips. Emits seconds, or `null` while the text is empty or unparseable.
 */
export function DurationInput({
  label = "Duration",
  value,
  onChange,
  className,
}: {
  label?: string;
  value: string;
  onChange: (text: string, seconds: number | null) => void;
  className?: string;
}) {
  const inputId = useId();
  const hintId = `${inputId}-hint`;
  const parsed = parseDuration(value);
  const seconds = "seconds" in parsed ? parsed.seconds : null;
  const errorMessage = "error" in parsed && parsed.error !== "empty" ? errorMessages[parsed.error] : null;
  const canonical = seconds === null ? null : formatDurationText(seconds);

  function set(text: string) {
    const result = parseDuration(text);
    onChange(text, "seconds" in result ? result.seconds : null);
  }

  return (
    <div className={cn("grid gap-2", className)}>
      <label className="grid gap-1 text-xs font-medium" htmlFor={inputId}>
        {label}
        <input
          id={inputId}
          type="text"
          inputMode="text"
          autoComplete="off"
          value={value}
          placeholder="1h 45m 44s"
          aria-describedby={hintId}
          aria-invalid={errorMessage ? true : undefined}
          className={cn(
            "rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
            errorMessage && "border-red-400 dark:border-red-800",
          )}
          onChange={(event) => set(event.target.value)}
        />
      </label>
      <p
        id={hintId}
        role={errorMessage ? "alert" : undefined}
        className={cn(
          "text-xs",
          errorMessage ? "text-red-700 dark:text-red-300" : "text-muted-foreground",
        )}
      >
        {errorMessage ?? (canonical ? `= ${canonical}` : "Type 1h 45m 44s, 90m or 1:45. A plain number means minutes.")}
      </p>
      <div className="flex flex-wrap gap-1.5">
        {canonical && canonical !== value.trim() ? (
          <button
            type="button"
            className={cn(chipClassName, "border-primary/40 text-primary")}
            onClick={() => set(canonical)}
          >
            Use {canonical}
          </button>
        ) : null}
        {PRESETS.map((preset) => (
          <button key={preset} type="button" className={chipClassName} onClick={() => set(preset)}>
            {preset}
          </button>
        ))}
      </div>
    </div>
  );
}
