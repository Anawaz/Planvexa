import { Suspense } from "react";
import { DocumentEditorPageClient } from "@/components/collab/DocumentEditorPageClient";

type DocumentPageProps = {
  params: Promise<{
    id: string;
  }>;
};

export default async function DocumentPage({ params }: DocumentPageProps) {
  const { id } = await params;

  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading document…
        </section>
      }
    >
      <DocumentEditorPageClient documentId={id} />
    </Suspense>
  );
}
