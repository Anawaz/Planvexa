"use client";

import Link from "next/link";
import { useState } from "react";
import { createPortal } from "react-dom";
import { Button } from "@/components/ui/Button";
import { HostSidebarNavigation, HostWordmark } from "./HostSidebar";

/**
 * The host console's top bar, matching the workspace shell's <c>Topbar</c>: same sticky translucent
 * header, same <c>min-h-16</c> row, same mobile Menu button opening the same style of left drawer —
 * because below `lg` the sidebar rail is hidden and the drawer is the only navigation there is.
 *
 * It carries far fewer controls than the workspace Topbar (no workspace switcher, presence, timer,
 * notifications or search — none of which mean anything without a workspace), so the space goes to the
 * standing reminder of where you are instead.
 */
export function HostTopbar() {
  const [mobileNavOpen, setMobileNavOpen] = useState(false);

  return (
    <header className="sticky top-0 z-30 border-b border-border bg-background/90 backdrop-blur">
      <div className="flex min-h-16 flex-wrap items-center gap-3 px-6 py-2 sm:px-8 lg:px-10 xl:flex-nowrap xl:gap-4 xl:py-0">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="lg:hidden"
          aria-haspopup="dialog"
          aria-expanded={mobileNavOpen}
          aria-controls="mobile-host-navigation"
          onClick={() => setMobileNavOpen(true)}
        >
          Menu
        </Button>
        <div className="lg:hidden">
          <HostWordmark />
        </div>

        <p className="hidden min-w-0 text-sm text-muted-foreground lg:block">
          <strong className="font-semibold text-foreground">Host administration.</strong>{" "}
          Changes here affect every workspace on this server.
        </p>

        <Link
          href="/app/my-work"
          className="ml-auto shrink-0 text-sm font-medium text-primary underline underline-offset-4 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          Back to your workspace
        </Link>
      </div>

      {/* The amber rule is the whole-page tell that this is not the workspace shell — it sits under a
          sticky header, so it stays visible while scrolling rather than being a banner you scroll past. */}
      <div className="h-0.5 bg-amber-500" aria-hidden="true" />

      {/* Portalled to <body> for the same reason as the workspace Topbar's drawer: this header has
          `backdrop-blur`, and a backdrop-filter makes the element a containing block for fixed
          descendants — so `fixed inset-0` measured the header, not the viewport, and the drawer was
          cropped to a strip near the top of the screen. */}
      {mobileNavOpen
        ? createPortal(
        <div className="fixed inset-0 z-50 lg:hidden" role="presentation">
          <button
            type="button"
            className="absolute inset-0 cursor-default bg-slate-950/50 backdrop-blur-sm pv-animate-backdrop"
            aria-label="Close navigation menu"
            onClick={() => setMobileNavOpen(false)}
          />
          <aside
            id="mobile-host-navigation"
            role="dialog"
            aria-modal="true"
            aria-label="Host administration navigation"
            className="absolute inset-y-0 left-0 flex w-80 max-w-[85vw] flex-col border-r border-border bg-card shadow-2xl pv-animate-drawer-left"
          >
            <div className="flex h-16 items-center justify-between border-b border-border px-4">
              <HostWordmark />
              <Button type="button" variant="ghost" size="sm" onClick={() => setMobileNavOpen(false)}>
                Close
              </Button>
            </div>
            <HostSidebarNavigation className="flex-1 px-4 py-4" onNavigate={() => setMobileNavOpen(false)} />
          </aside>
        </div>,
        document.body,
      )
        : null}
    </header>
  );
}
