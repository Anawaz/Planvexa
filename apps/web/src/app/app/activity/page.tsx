import { Suspense } from "react";
import { ActivityPageClient } from "@/components/work/ActivityPageClient";

export default function ActivityPage() {
  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading activity…
        </section>
      }
    >
      <ActivityPageClient />
    </Suspense>
  );
}
