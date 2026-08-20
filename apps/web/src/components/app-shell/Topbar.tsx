"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTheme, type Theme } from "@/app/providers";
import { apiClient } from "@/lib/api-client";
import { PresenceAvatars } from "@/components/collab/PresenceAvatars";
import { NotificationBell } from "@/components/notifications/NotificationBell";
import { GlobalTimerWidget } from "@/components/time/GlobalTimerWidget";
import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { useAppContext } from "@/lib/app-context/AppContext";
import { getHostAdminStatus } from "@/lib/host/client";
import { hostKeys } from "@/lib/host/queries";
import { SidebarNavigation } from "./Sidebar";
import { WorkspaceSwitcher } from "./WorkspaceSwitcher";

/** The palette itself lives in the app layout, next to the other shortcut dialogs. */
type TopbarProps = {
  searchOpen: boolean;
  onOpenSearch: () => void;
};

function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0])
    .join("")
    .toUpperCase();
}

export function Topbar({ searchOpen, onOpenSearch }: TopbarProps) {
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const { theme, setTheme } = useTheme();
  const { user, currentUser } = useAppContext();
  const displayName = currentUser?.displayName || user?.name || user?.email || "Account";
  const queryClient = useQueryClient();

  // Decides whether to offer the host console at all. Cheap, cached, and shared with the /host layout
  // through the same query key.
  const hostAdminQuery = useQuery({
    queryKey: hostKeys.status(),
    queryFn: getHostAdminStatus,
    staleTime: 5 * 60_000,
  });

  // Persists to the account (PATCH /users/me — same endpoint the profile page uses), so the
  // preference follows the user across devices/sessions instead of living only in this browser's
  // localStorage. The context update is immediate; the request is fire-and-forget best-effort (a
  // failure just means next login re-syncs from whatever the server still has).
  const themeMutation = useMutation({
    mutationFn: (next: Theme) =>
      apiClient.patch("/users/me", { displayName: currentUser?.displayName ?? displayName, theme: next }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["user", "me"] }),
  });

  function handleThemeChange(next: Theme) {
    setTheme(next);
    if (currentUser) {
      themeMutation.mutate(next);
    }
  }

  useEffect(() => {
    if (!mobileNavOpen) {
      return;
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setMobileNavOpen(false);
      }
    }

    document.addEventListener("keydown", handleKeyDown);
    document.body.style.overflow = "hidden";

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = "";
    };
  }, [mobileNavOpen]);

  return (
    <header className="sticky top-0 z-30 border-b border-border bg-background/90 backdrop-blur">
      {/* The row carries eight controls: below xl they wrap onto a second line instead of pushing
          the document wider than the viewport (which scrolled the whole app sideways). At xl the
          single row is back, and the shrinkable controls (selects, search) absorb the last pixels. */}
      <div className="flex min-h-16 flex-wrap items-center gap-3 px-6 py-2 sm:px-8 lg:px-10 xl:flex-nowrap xl:gap-4 xl:py-0">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="lg:hidden"
          aria-haspopup="dialog"
          aria-expanded={mobileNavOpen}
          aria-controls="mobile-primary-navigation"
          onClick={() => setMobileNavOpen(true)}
        >
          Menu
        </Button>
        <div className="font-semibold lg:hidden">Planvexa</div>
        <WorkspaceSwitcher />
        <PresenceAvatars />
        <button
          type="button"
          className="ml-auto hidden w-64 min-w-0 items-center justify-between gap-2 rounded-lg border border-border bg-card px-3 py-2 text-left text-sm text-muted-foreground shadow-sm transition hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring sm:flex"
          aria-haspopup="dialog"
          aria-expanded={searchOpen}
          onClick={onOpenSearch}
        >
          <span className="truncate">Search or jump to…</span>
          <kbd className="shrink-0 rounded border border-border bg-muted px-1.5 py-0.5 text-xs">
            Ctrl K
          </kbd>
        </button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="sm:hidden"
          aria-haspopup="dialog"
          aria-expanded={searchOpen}
          onClick={onOpenSearch}
        >
          Search
        </Button>
        <GlobalTimerWidget />
        <NotificationBell />
        <details className="relative shrink-0">
          <summary className="flex cursor-pointer list-none items-center gap-2 rounded-full border border-border bg-card px-3 py-2 text-sm font-medium shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring [&::-webkit-details-marker]:hidden">
            <Avatar
              avatarUrl={currentUser?.avatarUrl}
              initials={initials(displayName)}
              className="grid size-7 shrink-0 place-items-center rounded-full bg-primary text-xs font-semibold text-primary-foreground"
            />
            <span className="hidden max-w-32 truncate sm:inline">{displayName}</span>
          </summary>
          <div
            className="absolute right-0 mt-2 w-56 rounded-lg border border-border bg-card p-2 text-sm shadow-xl"
            role="menu"
          >
            {user?.email ? (
              <p className="px-2 py-2 text-xs text-muted-foreground">{user.email}</p>
            ) : null}
            <a
              href="/app/settings/profile"
              className="block w-full rounded-md px-2 py-2 text-left hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
              role="menuitem"
            >
              Edit profile
            </a>
            <label
              htmlFor="topbar-theme"
              className="flex items-center justify-between gap-2 rounded-md px-2 py-2 text-left"
            >
              Theme
              <select
                id="topbar-theme"
                value={theme}
                onChange={(event) => handleThemeChange(event.currentTarget.value as Theme)}
                className="rounded-md border border-border bg-background px-2 py-1 text-xs shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              >
                <option value="light">Light</option>
                <option value="dark">Dark</option>
                <option value="system">System</option>
              </select>
            </label>
            <a
              href="/legal"
              className="block w-full rounded-md px-2 py-2 text-left hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
              role="menuitem"
            >
              Legal and source code
            </a>
            {/* Only rendered for an instance-level administrator — the probe returns false for
                everyone else rather than a 403, so nothing here has to swallow an error. Hiding the
                link is presentational; the API's HostAdmin policy is the actual gate. */}
            {hostAdminQuery.data?.isHostAdmin ? (
              <a
                href="/host"
                className="block w-full rounded-md px-2 py-2 text-left hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
                role="menuitem"
              >
                Host administration
              </a>
            ) : null}
            {/* GET route: it clears the session cookies and redirects to Keycloak's end-session endpoint. */}
            <a
              href="/auth/logout"
              className="block w-full rounded-md px-2 py-2 text-left text-muted-foreground hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
              role="menuitem"
            >
              Sign out
            </a>
          </div>
        </details>
      </div>
      {mobileNavOpen ? (
        <div className="fixed inset-0 z-50 lg:hidden" role="presentation">
          <button
            type="button"
            className="absolute inset-0 cursor-default bg-slate-950/50 backdrop-blur-sm pv-animate-backdrop"
            aria-label="Close navigation menu"
            onClick={() => setMobileNavOpen(false)}
          />
          <aside
            id="mobile-primary-navigation"
            role="dialog"
            aria-modal="true"
            aria-label="Primary navigation"
            className="absolute inset-y-0 left-0 flex w-80 max-w-[85vw] flex-col border-r border-border bg-card shadow-2xl pv-animate-drawer-left"
          >
            <div className="flex h-16 items-center justify-between border-b border-border px-4">
              <span className="text-lg font-semibold tracking-tight">Planvexa</span>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => setMobileNavOpen(false)}
              >
                Close
              </Button>
            </div>
            <SidebarNavigation
              className="flex-1 px-4 py-4"
              onNavigate={() => setMobileNavOpen(false)}
            />
          </aside>
        </div>
      ) : null}
    </header>
  );
}
