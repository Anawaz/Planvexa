"use client";

import { useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { HostSidebar } from "@/components/host/HostSidebar";
import { HostTopbar } from "@/components/host/HostTopbar";
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

  // Same shape as the workspace shell (see app/app/layout.tsx): a fixed 18rem rail, the content
  // column offset by lg:pl-72, and a sticky top bar that owns the mobile drawer.
  return (
    <div className="min-h-screen bg-background">
      <a
        href="#host-content"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-card focus:px-4 focus:py-2 focus:text-sm focus:font-medium focus:shadow-lg"
      >
        Skip to main content
      </a>
      <HostSidebar />
      <div className="lg:pl-72">
        <HostTopbar />
        <main id="host-content" className="px-6 py-8 sm:px-8 lg:px-10">
          {children}
        </main>
      </div>
    </div>
  );
}
