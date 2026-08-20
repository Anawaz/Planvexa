"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { HostNav } from "@/components/host/host-ui";
import { getHostAdminStatus } from "@/lib/host/client";
import { hostKeys } from "@/lib/host/queries";

/**
 * The host administration console's shell.
 *
 * Deliberately OUTSIDE `/app`: that layout hard-gates on a resolved `workspaceId` and would never
 * render an instance-level page (a host administrator is typically a member of no workspace at all).
 * This shell requires no workspace and never sets one.
 *
 * The gate here is presentational — it decides what to render, not what is permitted. Every
 * `/api/v1/host/*` call is enforced server-side by the HostAdmin policy plus the host-admin RLS
 * policies, so a user who defeats this client-side check reaches exactly nothing.
 *
 * Visually distinct from the workspace shell (the amber rail and banner) on purpose: actions taken
 * here affect every workspace on the server, and the operator should never be in doubt about which
 * console they are looking at.
 */
export default function HostLayout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const statusQuery = useQuery({
    queryKey: hostKeys.status(),
    queryFn: getHostAdminStatus,
    // Authorization is re-evaluated per request server-side; refetching on focus means a revoked grant
    // stops rendering the console rather than leaving a shell whose every request 403s.
    staleTime: 30_000,
  });

  const isHostAdmin = statusQuery.data?.isHostAdmin ?? false;

  useEffect(() => {
    if (!statusQuery.isLoading && !isHostAdmin) {
      router.replace("/access-denied");
    }
  }, [statusQuery.isLoading, isHostAdmin, router]);

  if (statusQuery.isLoading || !isHostAdmin) {
    return (
      <div className="grid min-h-screen place-items-center bg-background">
        <p className="text-sm text-muted-foreground">Checking host administration access…</p>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <a
        href="#host-content"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-card focus:px-4 focus:py-2 focus:text-sm focus:font-medium focus:shadow-lg"
      >
        Skip to main content
      </a>

      <div className="border-b-2 border-amber-500 bg-amber-50 px-6 py-2 text-sm text-amber-900 dark:bg-amber-950 dark:text-amber-100">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-3">
          <span>
            <strong className="font-semibold">Host administration.</strong> Changes here affect every
            workspace on this server.
          </span>
          <Link href="/app/my-work" className="font-medium underline underline-offset-4">
            Back to your workspace
          </Link>
        </div>
      </div>

      <div className="mx-auto flex max-w-7xl flex-col gap-8 px-6 py-8 lg:flex-row">
        <aside className="lg:w-56 lg:shrink-0">
          <HostNav />
        </aside>
        <main id="host-content" className="min-w-0 flex-1">
          {children}
        </main>
      </div>
    </div>
  );
}
