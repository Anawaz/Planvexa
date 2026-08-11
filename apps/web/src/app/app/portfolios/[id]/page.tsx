import { Suspense } from "react";
import { PortfolioDetailPageClient } from "@/components/planning/PortfolioDetailPageClient";

type PortfolioPageProps = {
  params: Promise<{
    id: string;
  }>;
};

export default async function PortfolioPage({ params }: PortfolioPageProps) {
  const { id } = await params;

  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading portfolio…
        </section>
      }
    >
      <PortfolioDetailPageClient portfolioId={id} />
    </Suspense>
  );
}
