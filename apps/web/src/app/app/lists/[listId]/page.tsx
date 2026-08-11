import { Suspense } from "react";
import { ListPageClient } from "@/components/work/ListPageClient";

type ListPageProps = {
  params: Promise<{
    listId: string;
  }>;
};

export default async function ListPage({ params }: ListPageProps) {
  const { listId } = await params;

  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading list view…
        </section>
      }
    >
      <ListPageClient listId={listId} />
    </Suspense>
  );
}
