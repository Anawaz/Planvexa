import { Suspense } from "react";
import { WhiteboardEditorPageClient } from "@/components/collab/WhiteboardEditorPageClient";

type WhiteboardPageProps = {
  params: Promise<{
    id: string;
  }>;
};

export default async function WhiteboardPage({ params }: WhiteboardPageProps) {
  const { id } = await params;

  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading whiteboard…
        </section>
      }
    >
      <WhiteboardEditorPageClient whiteboardId={id} />
    </Suspense>
  );
}
