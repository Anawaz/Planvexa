"use client";

import { useState } from "react";
import { cn } from "@/lib/utils";
import { InlineComposer } from "./InlineComposer";

type AddTaskButtonProps = {
  label: string;
  buttonLabel?: string;
  /**
   * Accessible name for the collapsed button. Every group and column renders one, so without this
   * a screen reader hears "Add task" six times with nothing to tell them apart.
   */
  ariaLabel?: string;
  pending?: boolean;
  className?: string;
  onSubmit: (title: string) => void;
};

/**
 * The per-group / per-column create affordance: a button-shaped row that swaps itself for the
 * inline composer on click, so an empty text field never has to carry the discoverability.
 */
export function AddTaskButton({
  label,
  buttonLabel = "Add task",
  ariaLabel,
  pending = false,
  className,
  onSubmit,
}: AddTaskButtonProps) {
  const [open, setOpen] = useState(false);

  if (open) {
    return (
      <InlineComposer
        label={label}
        className={className}
        autoFocus
        pending={pending}
        onCancel={() => setOpen(false)}
        onSubmit={onSubmit}
      />
    );
  }

  return (
    <button
      type="button"
      aria-label={ariaLabel}
      className={cn(
        "flex w-full items-center gap-2 rounded-lg border border-dashed border-border px-3 py-2 text-left text-sm font-medium text-muted-foreground transition hover:border-primary hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
        className,
      )}
      onClick={() => setOpen(true)}
    >
      <span aria-hidden="true" className="text-base leading-none">
        +
      </span>
      {buttonLabel}
    </button>
  );
}
