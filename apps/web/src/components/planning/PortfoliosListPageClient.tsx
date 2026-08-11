"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { MemberSelect } from "@/components/people/MemberSelect";
import { useMembers } from "@/lib/members";
import { createPortfolio, deletePortfolio, listPortfolios } from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type { PortfolioStatus } from "@/lib/planning/types";
import { SpaceMultiSelect } from "./SpaceMultiSelect";

const STATUS_OPTIONS: PortfolioStatus[] = ["OnTrack", "AtRisk", "OffTrack"];

export function PortfoliosListPageClient() {
  const queryClient = useQueryClient();
  const { data: members } = useMembers();
  const [name, setName] = useState("");
  const [isPrivate, setIsPrivate] = useState(false);
  const [status, setStatus] = useState<PortfolioStatus>("OnTrack");
  const [ownerUserId, setOwnerUserId] = useState("");
  const [spaceIds, setSpaceIds] = useState<string[]>([]);
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);

  const portfoliosQuery = useQuery({
    queryKey: planningKeys.portfolios(),
    queryFn: listPortfolios,
  });
  const invalidatePortfolios = () =>
    queryClient.invalidateQueries({ queryKey: planningKeys.portfolios() });

  const createPortfolioMutation = useMutation({
    mutationFn: createPortfolio,
    onSuccess: () => {
      setName("");
      setIsPrivate(false);
      setStatus("OnTrack");
      setOwnerUserId("");
      setSpaceIds([]);
      void invalidatePortfolios();
    },
  });
  const deletePortfolioMutation = useMutation({
    mutationFn: deletePortfolio,
    onSuccess: () => {
      setPendingDeleteId(null);
      void invalidatePortfolios();
    },
  });
  const mutationError = createPortfolioMutation.error ?? deletePortfolioMutation.error;

  function memberLabel(userId: string) {
    const member = (members ?? []).find((m) => m.userId === userId);
    return member?.displayName ?? member?.email ?? userId;
  }

  function submitPortfolio(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!name.trim()) {
      return;
    }

    createPortfolioMutation.mutate({
      name: name.trim(),
      isPrivate,
      status,
      ownerUserId: ownerUserId || null,
      spaceIds,
    });
  }

  return (
    <section aria-labelledby="portfolios-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Reporting</p>
          <h1 id="portfolios-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Portfolios
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Named, owned groups of Spaces with their own Health/Progress/Milestones/Risks/Budget
            rollup, scoped to only the Spaces you curate into them.
          </p>
        </div>
        <Link href="/app/dashboards" className={buttonStyles({ variant: "outline", size: "sm" })}>
          Dashboards
        </Link>
      </div>

      {mutationError ? (
        <p
          role="alert"
          className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          This portfolio change could not be saved: {(mutationError as Error).message}
        </p>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
        <section className="grid gap-4 md:grid-cols-2" aria-label="Portfolio list">
          {portfoliosQuery.isLoading ? (
            <p className="rounded-[var(--radius)] border border-border bg-card p-4 text-sm text-muted-foreground">
              Loading portfolios…
            </p>
          ) : (portfoliosQuery.data ?? []).length === 0 ? (
            <EmptyState
              className="md:col-span-2"
              title="No portfolios yet"
              description="Create one with the form beside this list, choosing which Spaces it should roll up."
            />
          ) : (
            portfoliosQuery.data?.map((portfolio) => (
              <article
                key={portfolio.id}
                className="rounded-[var(--radius)] border border-border bg-card p-5 shadow-sm"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h2 className="truncate text-lg font-semibold">{portfolio.name}</h2>
                    <p className="mt-2 text-sm text-muted-foreground">
                      {portfolio.spaceIds.length} space{portfolio.spaceIds.length === 1 ? "" : "s"} ·{" "}
                      {portfolio.isPrivate ? "Private" : "Shared"} · Owner {memberLabel(portfolio.ownerUserId)}
                    </p>
                  </div>
                  <span className="rounded-full bg-primary/10 px-2.5 py-1 text-xs font-semibold text-primary">
                    {portfolio.status}
                  </span>
                </div>
                <div className="mt-4 flex flex-wrap items-center gap-2">
                  <Link
                    href={`/app/portfolios/${portfolio.id}`}
                    className={buttonStyles({ variant: "primary", size: "sm" })}
                  >
                    Open portfolio
                  </Link>
                  {pendingDeleteId === portfolio.id ? (
                    <>
                      <Button
                        type="button"
                        size="sm"
                        variant="outline"
                        className="border-red-300 text-red-700 dark:border-red-900 dark:text-red-400"
                        disabled={deletePortfolioMutation.isPending}
                        onClick={() => deletePortfolioMutation.mutate(portfolio.id)}
                      >
                        Confirm delete
                      </Button>
                      <Button type="button" size="sm" variant="ghost" onClick={() => setPendingDeleteId(null)}>
                        Keep
                      </Button>
                    </>
                  ) : (
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      className="text-red-600 hover:text-red-700 dark:text-red-400"
                      onClick={() => setPendingDeleteId(portfolio.id)}
                    >
                      Delete
                    </Button>
                  )}
                </div>
              </article>
            ))
          )}
        </section>

        <form
          onSubmit={submitPortfolio}
          className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
        >
          <h2 className="text-sm font-semibold">Create portfolio</h2>
          <p className="mt-1 text-xs text-muted-foreground">
            Pick the Spaces it should roll up — you can change these later.
          </p>
          <div className="mt-4 grid gap-3">
            <label className="grid gap-1 text-xs font-medium">
              Portfolio name
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
              <MemberSelect value={ownerUserId} onChange={setOwnerUserId} includeAny anyLabel="Me" />
            </label>
            <div className="text-xs font-medium">
              Spaces
              <SpaceMultiSelect value={spaceIds} onChange={setSpaceIds} />
            </div>
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={isPrivate}
                onChange={(event) => setIsPrivate(event.target.checked)}
                className="size-4 rounded border-border accent-[var(--primary)]"
              />
              Private to me
            </label>
            <Button type="submit" size="sm" disabled={createPortfolioMutation.isPending}>
              Create portfolio
            </Button>
          </div>
        </form>
      </div>
    </section>
  );
}
