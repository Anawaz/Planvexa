"use client";

import { useId, useRef } from "react";
import { Button } from "@/components/ui/Button";
import { useFocusTrap } from "@/components/work/useFocusTrap";

/** Rendered order is the order they are worth learning in. */
const shortcuts: Array<[keys: string[], description: string]> = [
  [["Ctrl", "K"], "Open the command palette (Cmd K on macOS)"],
  [["/"], "Search the workspace"],
  [["N"], "New task"],
  [["M"], "Go to My Work"],
  [["I"], "Go to Inbox"],
  [["?"], "Show this list"],
  [["Esc"], "Close the open dialog, drawer or menu"],
];

/**
 * The `?` sheet. Same modal plumbing as the quick-add dialog, so Escape closes it and Tab stays
 * inside — which is also what makes the `Esc` row below true.
 */
export function ShortcutsHelp({ onClose }: { onClose: () => void }) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const titleId = useId();

  useFocusTrap({ open: true, containerRef: dialogRef, onClose });

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center px-4 py-24" role="presentation">
      <button
        type="button"
        aria-label="Close keyboard shortcuts"
        className="absolute inset-0 cursor-default bg-slate-950/40 backdrop-blur-[1px] pv-animate-backdrop"
        onClick={onClose}
      />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className="relative w-full max-w-md overflow-hidden rounded-[var(--radius)] border border-border bg-card shadow-2xl outline-none pv-animate-command"
      >
        <div className="space-y-4 p-5">
          <h2 id={titleId} className="text-lg font-semibold">
            Keyboard shortcuts
          </h2>
          <dl className="divide-y divide-border text-sm">
            {shortcuts.map(([keys, description]) => (
              <div key={description} className="flex items-center justify-between gap-4 py-2.5">
                <dt className="text-muted-foreground">{description}</dt>
                <dd className="flex shrink-0 items-center gap-1">
                  {keys.map((key) => (
                    <kbd
                      key={key}
                      className="rounded border border-border bg-muted px-1.5 py-0.5 text-xs font-medium text-foreground"
                    >
                      {key}
                    </kbd>
                  ))}
                </dd>
              </div>
            ))}
          </dl>
          <p className="text-xs text-muted-foreground">
            Single-key shortcuts stay quiet while you type in a field or while a dialog is open.
          </p>
          <div className="flex justify-end border-t border-border pt-4">
            <Button type="button" size="sm" onClick={onClose}>
              Close
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
