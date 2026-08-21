"use client";

import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

/**
 * Closes on outside click and Escape — the two ways anyone expects a menu to go away.
 *
 * Lives here rather than in the editor toolbar (its first caller) so the two menus in the app share
 * one implementation of the dismiss contract instead of drifting apart.
 */
export function useDismissable(open: boolean, close: () => void) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: MouseEvent) {
      if (ref.current && !ref.current.contains(event.target as Node)) close();
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") close();
    }

    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open, close]);

  return ref;
}

export type ActionMenuItem = {
  label: string;
  onSelect: () => void;
  disabled?: boolean;
  /** Rendered in the destructive palette and separated from the rest of the list. */
  destructive?: boolean;
  /** For items that toggle a mode (Watch, Make private) so the menu reports current state. */
  pressed?: boolean;
};

/**
 * An overflow menu for a header's secondary actions.
 *
 * Exists because a row of eight buttons has no width at which it works: it overflowed the task
 * panel's own 42rem drawer on a desktop and ran clean off the side of a phone, and because the
 * drawer clips (`overflow-hidden`) the buttons that fell off — Close included — were simply gone.
 * One trigger of fixed width is the only arrangement that survives a 320px viewport.
 */
export function ActionMenu({
  label = "More actions",
  items,
  align = "right",
  className,
}: {
  label?: string;
  items: ActionMenuItem[];
  align?: "left" | "right";
  className?: string;
}) {
  const [open, setOpen] = useState(false);
  const ref = useDismissable(open, () => setOpen(false));
  const enabled = items.filter((item) => !item.destructive);
  const destructive = items.filter((item) => item.destructive);

  return (
    <div ref={ref} className={cn("relative shrink-0", className)}>
      <button
        type="button"
        title={label}
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
        className="inline-flex size-9 items-center justify-center rounded-lg border border-border bg-background text-foreground transition-[transform,background-color] duration-150 ease-out active:scale-[0.97] [@media(hover:hover)]:hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none motion-reduce:active:scale-100"
      >
        <svg viewBox="0 0 24 24" aria-hidden="true" className="size-4" fill="currentColor">
          <circle cx="5" cy="12" r="1.75" />
          <circle cx="12" cy="12" r="1.75" />
          <circle cx="19" cy="12" r="1.75" />
        </svg>
      </button>
      {open ? (
        <div
          role="menu"
          className={cn(
            "absolute z-30 mt-1 w-56 rounded-lg border border-border bg-card p-1 text-sm shadow-xl pv-animate-popover",
            align === "right" ? "right-0" : "left-0",
          )}
        >
          {enabled.map((item) => (
            <MenuItem key={item.label} item={item} onDone={() => setOpen(false)} />
          ))}
          {destructive.length > 0 && enabled.length > 0 ? (
            <div className="my-1 h-px bg-border" aria-hidden="true" />
          ) : null}
          {destructive.map((item) => (
            <MenuItem key={item.label} item={item} onDone={() => setOpen(false)} />
          ))}
        </div>
      ) : null}
    </div>
  );
}

function MenuItem({ item, onDone }: { item: ActionMenuItem; onDone: () => void }) {
  return (
    <button
      type="button"
      // A toggle in a menu is a menuitemcheckbox, not a menuitem — `menuitem` has no state to
      // report, so a screen reader would announce "Make private" identically whether or not the
      // task already is.
      role={item.pressed === undefined ? "menuitem" : "menuitemcheckbox"}
      aria-checked={item.pressed}
      disabled={item.disabled}
      onClick={() => {
        item.onSelect();
        onDone();
      }}
      className={cn(
        "block w-full rounded-md px-2 py-2 text-left disabled:cursor-not-allowed disabled:opacity-50 [@media(hover:hover)]:hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring",
        item.destructive && "text-red-600 dark:text-red-400",
      )}
    >
      {item.label}
    </button>
  );
}
