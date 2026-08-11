import { Suspense } from "react";
import { InboxPageClient } from "@/components/work/InboxPageClient";

export default function InboxPage() {
  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading inbox…
        </section>
      }
    >
      <InboxPageClient />
    </Suspense>
  );
}
