import { Suspense } from "react";
import { ClipDetailPageClient } from "@/components/collab/ClipDetailPageClient";

type ClipPageProps = {
  params: Promise<{
    id: string;
  }>;
};

export default async function ClipPage({ params }: ClipPageProps) {
  const { id } = await params;

  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading clip…
        </section>
      }
    >
      <ClipDetailPageClient clipId={id} />
    </Suspense>
  );
}
