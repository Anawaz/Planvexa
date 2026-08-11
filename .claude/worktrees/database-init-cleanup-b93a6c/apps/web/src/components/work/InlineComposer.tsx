"use client";

import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { cn } from "@/lib/utils";

type InlineComposerProps = {
  label: string;
  submitLabel?: string;
  pending?: boolean;
  className?: string;
  autoFocus?: boolean;
  /** Rendered as a Cancel button and bound to Escape when provided. */
  onCancel?: () => void;
  onSubmit: (value: string) => void;
};

/** One-line "type a title, press Add" form — task composers, subtasks, checklists, checklist items. */
export function InlineComposer({
  label,
  submitLabel = "Add",
  pending = false,
  className,
  autoFocus = false,
  onCancel,
  onSubmit,
}: InlineComposerProps) {
  const [value, setValue] = useState("");
  const trimmed = value.trim();

  return (
    <form
      className={cn("flex items-center gap-2", className)}
      onSubmit={(event) => {
        event.preventDefault();

        if (!trimmed) {
          return;
        }

        onSubmit(trimmed);
        setValue("");
      }}
    >
      <input
        aria-label={label}
        placeholder={label}
        value={value}
        disabled={pending}
        // Opt-in only: set when a click has just revealed this field, so focus follows the intent.
        autoFocus={autoFocus}
        className="h-9 min-w-0 flex-1 rounded-lg border border-border bg-background px-3 text-sm outline-none placeholder:text-muted-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:opacity-50"
        onChange={(event) => setValue(event.currentTarget.value)}
        onKeyDown={(event) => {
          if (event.key === "Escape" && onCancel) {
            event.stopPropagation();
            onCancel();
          }
        }}
      />
      <Button type="submit" size="sm" variant="secondary" disabled={pending || !trimmed}>
        {submitLabel}
      </Button>
      {onCancel ? (
        <Button type="button" size="sm" variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      ) : null}
    </form>
  );
}
