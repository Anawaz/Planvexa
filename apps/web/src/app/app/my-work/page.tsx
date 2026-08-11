import { Suspense } from "react";
import { MyWorkPageClient } from "@/components/work/MyWorkPageClient";

export default function MyWorkPage() {
  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading assignments…
        </section>
      }
    >
      <MyWorkPageClient />
    </Suspense>
  );
}
