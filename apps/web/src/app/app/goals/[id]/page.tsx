import { Suspense } from "react";
import { GoalDetailPageClient } from "@/components/goals/GoalDetailPageClient";

type GoalPageProps = {
  params: Promise<{
    id: string;
  }>;
};

export default async function GoalPage({ params }: GoalPageProps) {
  const { id } = await params;

  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading goal…
        </section>
      }
    >
      <GoalDetailPageClient goalId={id} />
    </Suspense>
  );
}
