"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FormEvent, useState } from "react";
import { Button } from "@/components/ui/Button";
import { createBudget, deleteBudget, getBudgetStatus, listBudgets } from "@/lib/time/client";
import { moneyFormatter, toDateInputValue } from "@/lib/time/format";
import { timeKeys } from "@/lib/time/queries";
import type { Budget, BudgetScopeType } from "@/lib/time/types";

function startOfCurrentWeek() {
  const today = new Date();
  const start = new Date(today);
  const diff = (start.getDay() - 1 + 7) % 7;
  start.setDate(start.getDate() - diff);
  start.setHours(0, 0, 0, 0);
  return start;
}

function endOfCurrentWeek() {
  const end = startOfCurrentWeek();
  end.setDate(end.getDate() + 6);
  end.setHours(23, 59, 59, 999);
  return end;
}

function dateRangeToIso(fromDate: string, toDate: string) {
  return {
    from: new Date(`${fromDate}T00:00:00`).toISOString(),
    to: new Date(`${toDate}T23:59:59`).toISOString(),
  };
}

const inputClassName =
  "rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

/** One budget row: cap summary + an on-demand consumption/profitability lookup for a date range. */
function BudgetRow({ budget, from, to }: { budget: Budget; from: string; to: string }) {
  const queryClient = useQueryClient();
  const [expanded, setExpanded] = useState(false);
  const statusQuery = useQuery({
    queryKey: timeKeys.budgetStatus(budget.id, { from, to }),
    queryFn: () => getBudgetStatus(budget.id, { from, to }),
    enabled: expanded,
  });
  const deleteMutation = useMutation({
    mutationFn: () => deleteBudget(budget.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: timeKeys.budgets() }),
  });
  const status = statusQuery.data;

  return (
    <li className="rounded-lg border border-border bg-background p-3 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <p className="font-medium">{budget.name}</p>
          <p className="text-xs text-muted-foreground">
            {budget.scopeType} {budget.scopeId.slice(0, 8)}
            {budget.monetaryCapAmount != null ? ` · ${moneyFormatter.format(budget.monetaryCapAmount)} cap` : ""}
            {budget.timeCapSeconds != null ? ` · ${(budget.timeCapSeconds / 3600).toFixed(0)}h cap` : ""}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button type="button" size="sm" variant="outline" onClick={() => setExpanded((v) => !v)}>
            {expanded ? "Hide status" : "View status"}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            className="text-red-600 hover:text-red-700 dark:text-red-400"
            disabled={deleteMutation.isPending}
            onClick={() => deleteMutation.mutate()}
          >
            Delete
          </Button>
        </div>
      </div>

      {expanded ? (
        statusQuery.isLoading ? (
          <p className="mt-3 text-xs text-muted-foreground">Loading status…</p>
        ) : status ? (
          <div className="mt-3 grid grid-cols-2 gap-3 border-t border-border pt-3 sm:grid-cols-4">
            <div>
              <p className="text-xs text-muted-foreground">Hours</p>
              <p className="font-semibold">{status.hours.toFixed(2)}</p>
            </div>
            <div>
              <p className="text-xs text-muted-foreground">Cost</p>
              <p className="font-semibold">{moneyFormatter.format(status.cost)}</p>
            </div>
            <div>
              <p className="text-xs text-muted-foreground">Revenue</p>
              <p className="font-semibold">{moneyFormatter.format(status.revenue)}</p>
            </div>
            <div>
              <p className="text-xs text-muted-foreground">Profit</p>
              <p className="font-semibold">{moneyFormatter.format(status.profit)}</p>
            </div>
            {status.monetaryConsumedPercent != null ? (
              <div>
                <p className="text-xs text-muted-foreground">Budget used</p>
                <p className="font-semibold">{status.monetaryConsumedPercent.toFixed(1)}%</p>
              </div>
            ) : null}
            {status.timeConsumedPercent != null ? (
              <div>
                <p className="text-xs text-muted-foreground">Time used</p>
                <p className="font-semibold">{status.timeConsumedPercent.toFixed(1)}%</p>
              </div>
            ) : null}
          </div>
        ) : null
      ) : null}
    </li>
  );
}

export default function BudgetsPage() {
  const queryClient = useQueryClient();
  const [fromDate, setFromDate] = useState(() => toDateInputValue(startOfCurrentWeek()));
  const [toDate, setToDate] = useState(() => toDateInputValue(endOfCurrentWeek()));
  const [scopeType, setScopeType] = useState<BudgetScopeType>("List");
  const [scopeId, setScopeId] = useState("");
  const [name, setName] = useState("");
  const [monetaryCap, setMonetaryCap] = useState("");
  const [timeCapHours, setTimeCapHours] = useState("");

  const budgetsQuery = useQuery({ queryKey: timeKeys.budgets(), queryFn: listBudgets });
  const createMutation = useMutation({
    mutationFn: createBudget,
    onSuccess: () => {
      setName("");
      setScopeId("");
      setMonetaryCap("");
      setTimeCapHours("");
      void queryClient.invalidateQueries({ queryKey: timeKeys.budgets() });
    },
  });

  const { from, to } = dateRangeToIso(fromDate, toDate);
  const budgets = budgetsQuery.data ?? [];

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const monetaryCapAmount = monetaryCap.trim() ? Number(monetaryCap) : null;
    const timeCapSeconds = timeCapHours.trim() ? Math.round(Number(timeCapHours) * 3600) : null;
    if (!name.trim() || !scopeId.trim() || (monetaryCapAmount == null && timeCapSeconds == null)) {
      return;
    }

    createMutation.mutate({ scopeType, scopeId: scopeId.trim(), name: name.trim(), monetaryCapAmount, timeCapSeconds });
  }

  return (
    <section aria-labelledby="budgets-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Budgets & profitability</p>
        <h1 id="budgets-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Budgets
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Set a monetary and/or time cap for a Space or List, then check consumption and profitability
          for a date range. Requires administrator access — the same as rates and reports.
        </p>
      </div>

      {budgetsQuery.isError || createMutation.isError ? (
        <p
          role="alert"
          className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          {(budgetsQuery.error ?? createMutation.error) instanceof Error
            ? (budgetsQuery.error ?? createMutation.error as Error).message
            : "This action requires administrator access."}
        </p>
      ) : null}

      <section className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
        <h2 className="text-sm font-semibold">New budget</h2>
        <p className="mt-1 text-xs text-muted-foreground">
          Paste the Space or List id from its page URL — there is no picker here yet.
        </p>
        <form className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-5" onSubmit={handleSubmit}>
          <label className="grid gap-1 text-xs font-medium">
            Scope
            <select value={scopeType} className={inputClassName} onChange={(event) => setScopeType(event.target.value as BudgetScopeType)}>
              <option value="List">List</option>
              <option value="Space">Space</option>
            </select>
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Scope id
            <input type="text" value={scopeId} placeholder="Space/List id" className={inputClassName} onChange={(event) => setScopeId(event.target.value)} />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Name
            <input type="text" value={name} placeholder="Q1 delivery" className={inputClassName} onChange={(event) => setName(event.target.value)} />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Monetary cap
            <input type="number" min={0} value={monetaryCap} placeholder="e.g. 10000" className={inputClassName} onChange={(event) => setMonetaryCap(event.target.value)} />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Time cap (hours)
            <input type="number" min={0} value={timeCapHours} placeholder="e.g. 200" className={inputClassName} onChange={(event) => setTimeCapHours(event.target.value)} />
          </label>
          <div className="sm:col-span-2 lg:col-span-5">
            <Button type="submit" size="sm" disabled={createMutation.isPending}>
              Create budget
            </Button>
          </div>
        </form>
      </section>

      <section className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
        <div className="flex flex-wrap items-end gap-3">
          <label className="grid gap-1 text-xs font-medium">
            From
            <input type="date" value={fromDate} className={inputClassName} onChange={(event) => setFromDate(event.target.value)} />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            To
            <input type="date" value={toDate} className={inputClassName} onChange={(event) => setToDate(event.target.value)} />
          </label>
        </div>

        <ul className="mt-4 space-y-2">
          {budgetsQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">Loading budgets…</p>
          ) : budgets.length === 0 ? (
            <p className="rounded-lg border border-dashed border-border p-3 text-sm text-muted-foreground">
              No budgets yet.
            </p>
          ) : (
            budgets.map((budget) => <BudgetRow key={budget.id} budget={budget} from={from} to={to} />)
          )}
        </ul>
      </section>
    </section>
  );
}
