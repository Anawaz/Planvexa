import { cn } from "@/lib/utils";

type BadgeTone = "blue" | "green" | "red" | "slate" | "yellow";

const toneClasses: Record<BadgeTone, string> = {
  blue: "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200",
  green: "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200",
  red: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200",
  slate: "bg-muted text-muted-foreground",
  yellow: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
};

const statusTones: Record<string, BadgeTone> = {
  Active: "green",
  Completed: "green",
  Draft: "slate",
  Failed: "red",
  Open: "blue",
  Pending: "yellow",
  Running: "blue",
};

export function StatusBadge({ status, tone }: { status: string; tone?: BadgeTone }) {
  return (
    <span
      className={cn(
        "inline-flex rounded-full px-2.5 py-1 text-xs font-semibold",
        toneClasses[tone ?? statusTones[status] ?? "slate"],
      )}
    >
      {status}
    </span>
  );
}
