import type { ReactNode } from "react";
import { ApiError } from "@/lib/api-client";
import { cn } from "@/lib/utils";
import { Button } from "./Button";

/** The subset of a react-query `UseQueryResult` this needs — matches `useQuery(...)`'s return value
 * directly, so callers never have to massage it into a bespoke shape. */
export type QueryStateResult = {
  isLoading: boolean;
  isError: boolean;
  error?: unknown;
  refetch: () => unknown;
};

const cardClassName = "rounded-[var(--radius)] border border-border bg-card p-6";

/** ApiError.status -> copy. Anything not listed here (including non-ApiError/network failures)
 * falls through to the generic "could not be loaded" case with a retry button. */
function errorCopy(error: unknown): { title: string; description: string; canRetry: boolean } {
  if (error instanceof ApiError) {
    switch (error.status) {
      case 403:
        return { title: "Access denied", description: "You do not have permission to view this.", canRetry: false };
      case 404:
        return { title: "Not found", description: "This may have been deleted or moved.", canRetry: false };
      case 429:
        return { title: "Rate limited", description: "Too many requests — wait a moment and try again.", canRetry: true };
      default:
        break;
    }
  }

  return {
    title: "Something went wrong",
    description: "This could not be loaded. Check your connection and try again.",
    canRetry: true,
  };
}

/**
 * The one loading/error/ready wrapper every list/detail page should render its query through —
 * matches ListPageClient's original inline pattern, but shared so `query.isError` can never be
 * silently skipped (which otherwise renders a misleading "empty" page on a real API failure).
 * The true zero-data empty state stays the caller's own (each page's copy/actions differ), so this
 * only covers loading and error; render `children` once `query.isLoading`/`isError` are both false.
 */
export function QueryState({
  query,
  loadingLabel = "Loading…",
  className,
  children,
}: {
  query: QueryStateResult;
  loadingLabel?: string;
  className?: string;
  children: ReactNode;
}) {
  if (query.isLoading) {
    return (
      <section className={cn(cardClassName, "text-sm text-muted-foreground", className)}>{loadingLabel}</section>
    );
  }

  if (query.isError) {
    const { title, description, canRetry } = errorCopy(query.error);
    return (
      <section className={cn(cardClassName, className)} role="alert">
        <h2 className="text-lg font-semibold">{title}</h2>
        <p className="mt-2 text-sm text-muted-foreground">{description}</p>
        {canRetry ? (
          <Button type="button" variant="outline" size="sm" className="mt-4" onClick={() => query.refetch()}>
            Retry
          </Button>
        ) : null}
      </section>
    );
  }

  return <>{children}</>;
}
