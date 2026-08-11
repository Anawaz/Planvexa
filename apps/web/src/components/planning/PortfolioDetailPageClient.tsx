"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/Button";
import { MemberSelect } from "@/components/people/MemberSelect";
import { useMembers } from "@/lib/members";
import { deletePortfolio, getPortfolioReport, listPortfolios, updatePortfolio } from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type { PortfolioStatus } from "@/lib/planning/types";
import { useRecordRecentView } from "@/lib/recent/useRecordRecentView";
import { PortfolioHealthTable } from "./PortfolioHealthTable";
import { SpaceMultiSelect } from "./SpaceMultiSelect";

const STATUS_OPTIONS: PortfolioStatus[] = ["OnTrack", "AtRisk", "OffTrack"];

// No dedicated GET /portfolios/{id} for the entity itself (only its rollup) -- list + find, same as
// how DashboardDetailPageClient resolves its dashboard from the list-backed cache would if it needed to.
function toDateInput(value: string | null) {
  return value ? value.slice(0, 10) : "";
}

export function PortfolioDetailPageClient({ portfolioId }: { portfolioId: string }) {
  useRecordRecentView("portfolio", portfolioId);
  const router = useRouter();
  const queryClient = useQueryClient();
  const { data: members } = useMembers();

  const portfoliosQuery = useQuery({ queryKey: planningKeys.portfolios(), queryFn: listPortfolios });
  const portfolio = portfoliosQuery.data?.find((p) => p.id === portfolioId);

  const reportQuery = useQuery({
    queryKey: planningKeys.portfolioReport(portfolioId, {}),
    queryFn: () => getPortfolioReport(portfolioId),
  });

  const [name, setName] = useState("");
  const [status, setStatus] = useState<PortfolioStatus>("OnTrack");
  const [ownerUserId, setOwnerUserId] = useState("");
  const [isPrivate, setIsPrivate] = useState(false);
  const [startUtc, setStartUtc] = useState("");
  const [targetEndUtc, setTargetEndUtc] = useState("");
  const [spaceIds, setSpaceIds] = useState<string[]>([]);
  const initializedRef = useRef(false);

  useEffect(() => {
    if (!initializedRef.current && portfolio) {
      setName(portfolio.name);
      setStatus(portfolio.status);
      setOwnerUserId(portfolio.ownerUserId);
      setIsPrivate(portfolio.isPrivate);
      setStartUtc(toDateInput(portfolio.startUtc));
      setTargetEndUtc(toDateInput(portfolio.targetEndUtc));
      setSpaceIds(portfolio.spaceIds);
      initializedRef.current = true;
    }
  }, [portfolio]);

  const updateMutation = useMutation({
    mutationFn: () =>
      updatePortfolio(portfolioId, {
        name: name.trim(),
        status,
        ownerUserId: ownerUserId || null,
        isPrivate,
        startUtc: startUtc ? new Date(startUtc).toISOString() : null,
        targetEndUtc: targetEndUtc ? new Date(targetEndUtc).toISOString() : null,
        spaceIds,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: planningKeys.portfolios() });
      void queryClient.invalidateQueries({ queryKey: planningKeys.portfolioReport(portfolioId, {}) });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => deletePortfolio(portfolioId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: planningKeys.portfolios() });
      router.push("/app/portfolios");
    },
  });

  if (portfoliosQuery.isLoading) {
    return (
      <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
        Loading portfolio…
      </section>
    );
  }

  if (!portfolio) {
    return (
      <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
        Portfolio not found (or private to another member).
      </section>
    );
  }

  return (
    <section aria-labelledby="portfolio-detail-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Reporting · Portfolio</p>
          <h1 id="portfolio-detail-title" className="mt-2 text-3xl font-semibold tracking-tight">
            {portfolio.name}
          </h1>
        </div>
        <Button
          type="button"
          size="sm"
          variant="outline"
          className="border-red-300 text-red-700 dark:border-red-900 dark:text-red-400"
          disabled={deleteMutation.isPending}
          onClick={() => deleteMutation.mutate()}
        >
          Delete portfolio
        </Button>
      </div>

      <section
        className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
        aria-labelledby="portfolio-settings-title"
      >
        <h2 id="portfolio-settings-title" className="text-sm font-semibold">
          Settings
        </h2>
        <div className="mt-4 grid gap-3 md:grid-cols-2">
          <label className="grid gap-1 text-xs font-medium">
            Name
            <input
              value={name}
              onChange={(event) => setName(event.target.value)}
              className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Status
            <select
              value={status}
              onChange={(event) => setStatus(event.target.value as PortfolioStatus)}
              className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              {STATUS_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Owner
            <MemberSelect value={ownerUserId} onChange={setOwnerUserId} />
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={isPrivate}
              onChange={(event) => setIsPrivate(event.target.checked)}
              className="size-4 rounded border-border accent-[var(--primary)]"
            />
            Private to owner
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Start date
            <input
              type="date"
              value={startUtc}
              onChange={(event) => setStartUtc(event.target.value)}
              className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Target end date
            <input
              type="date"
              value={targetEndUtc}
              onChange={(event) => setTargetEndUtc(event.target.value)}
              className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            />
          </label>
          <div className="text-xs font-medium md:col-span-2">
            Spaces in this portfolio
            <SpaceMultiSelect value={spaceIds} onChange={setSpaceIds} />
          </div>
        </div>
        {updateMutation.error ? (
          <p role="alert" className="mt-3 text-sm text-red-700 dark:text-red-400">
            Could not save: {(updateMutation.error as Error).message}
          </p>
        ) : null}
        <Button
          type="button"
          size="sm"
          className="mt-4"
          disabled={!name.trim() || updateMutation.isPending}
          onClick={() => updateMutation.mutate()}
        >
          Save changes
        </Button>
        <p className="mt-2 text-xs text-muted-foreground">
          {(members ?? []).length === 0 ? null : "Owner and Admins can edit; others can only view (if not private)."}
        </p>
      </section>

      <PortfolioHealthTable
        title="Rollup"
        description="Scoped to only this portfolio's curated Spaces, not the whole workspace."
        rows={reportQuery.data ?? []}
        isLoading={reportQuery.isLoading}
      />
    </section>
  );
}
