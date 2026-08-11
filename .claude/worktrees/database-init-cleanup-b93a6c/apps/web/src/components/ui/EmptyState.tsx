import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

type EmptyStateProps = {
  title: string;
  description: string;
  className?: string;
  /** The action that fills the void — a Button or Link. Omit for read-only surfaces. */
  children?: ReactNode;
};

/**
 * The one "nothing here yet" block: what is missing, and the control that fixes it.
 * Every zero-state in the app renders through this so they read the same in both themes.
 */
export function EmptyState({ title, description, className, children }: EmptyStateProps) {
  return (
    <div
      className={cn(
        "rounded-xl border border-dashed border-border bg-background p-6 text-sm text-muted-foreground",
        className,
      )}
    >
      <p className="font-medium text-foreground">{title}</p>
      <p className="mt-2 max-w-prose leading-6">{description}</p>
      {children ? <div className="mt-4 flex flex-wrap items-center gap-3">{children}</div> : null}
    </div>
  );
}
