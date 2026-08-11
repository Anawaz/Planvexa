import { Suspense } from "react";
import { DashboardDetailPageClient } from "@/components/planning/DashboardDetailPageClient";

type DashboardPageProps = {
  params: Promise<{
    id: string;
  }>;
};

export default async function DashboardPage({ params }: DashboardPageProps) {
  const { id } = await params;

  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading dashboard…
        </section>
      }
    >
      <DashboardDetailPageClient dashboardId={id} />
    </Suspense>
  );
}
