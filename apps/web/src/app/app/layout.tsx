"use client";

import { useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { useTheme } from "@/app/providers";
import { CommandPalette } from "@/components/app-shell/CommandPalette";
import { MfaRequiredScreen } from "@/components/app-shell/MfaRequiredScreen";
import { ShortcutsHelp } from "@/components/app-shell/ShortcutsHelp";
import { Sidebar } from "@/components/app-shell/Sidebar";
import { Topbar } from "@/components/app-shell/Topbar";
import { QuickAddTask } from "@/components/work/QuickAddTask";
import { useAppContext } from "@/lib/app-context/AppContext";
import { OfflineIndicator } from "@/components/app-shell/OfflineIndicator";
import { useOfflineSync } from "@/lib/offline/useOfflineSync";
import { useRealtime } from "@/lib/realtime/useRealtime";
import { useGlobalShortcuts, type ShortcutAction } from "@/lib/shortcuts/useGlobalShortcuts";

export default function AuthenticatedAppLayout({
  children,
}: {
  children: ReactNode;
}) {
  // One gate instead of `enabled:` on ~40 queries below (Topbar widgets included):
  // nothing may fetch before the workspace context exists.
  const { workspaceId, workspaces, isLoading, mfaRequired, currentUser } = useAppContext();
  const router = useRouter();

  // Reconcile ThemeContext with the account's server-side preference once, right after login —
  // localStorage (read synchronously by Providers for the pre-auth/instant-paint fallback) is what
  // renders until this fires. Only ever runs once per sign-in; the ref keeps a later local theme
  // change (via Topbar) from being clobbered by this effect re-running.
  const { setTheme } = useTheme();
  const themeSyncedRef = useRef(false);
  useEffect(() => {
    if (themeSyncedRef.current || !currentUser) {
      return;
    }

    themeSyncedRef.current = true;
    if (currentUser.theme === "light" || currentUser.theme === "dark" || currentUser.theme === "system") {
      setTheme(currentUser.theme);
    }
  }, [currentUser, setTheme]);

  // A freshly authenticated user who has never created/joined a workspace has nothing to select —
  // workspaceId can never become truthy on its own, which otherwise leaves them stuck on "Loading
  // workspace…" forever instead of reaching onboarding.
  useEffect(() => {
    if (!isLoading && !workspaceId && workspaces.length === 0) {
      router.replace("/onboarding");
    }
  }, [isLoading, workspaceId, workspaces.length, router]);
  // The three shortcut dialogs share one slot: opening any of them closes the others, and the
  // action names double as the state, so the shortcut hook can be handed setDialog directly.
  const [dialog, setDialog] = useState<"search" | "quickAdd" | "help" | null>(null);
  // The single workspace hub connection for the whole authenticated shell.
  useRealtime();
  // Replays the offline mutation outbox on reconnect.
  useOfflineSync();

  const handleShortcut = useCallback(
    (action: ShortcutAction) => {
      if (action === "myWork") {
        router.push("/app/my-work");
        return;
      }

      if (action === "inbox") {
        router.push("/app/inbox");
        return;
      }

      setDialog(action);
    },
    [router],
  );

  useGlobalShortcuts(handleShortcut);

  if (mfaRequired) {
    return <MfaRequiredScreen />;
  }

  if (!workspaceId) {
    return (
      <div className="grid min-h-screen place-items-center bg-background">
        <p className="text-sm text-muted-foreground">Loading workspace…</p>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-card focus:px-4 focus:py-2 focus:text-sm focus:font-medium focus:shadow-lg"
      >
        Skip to main content
      </a>
      <Sidebar />
      <div className="lg:pl-72">
        <Topbar searchOpen={dialog === "search"} onOpenSearch={() => setDialog("search")} />
        <OfflineIndicator />
        <main id="main-content" className="px-6 py-8 sm:px-8 lg:px-10">
          {children}
        </main>
      </div>
      <CommandPalette
        open={dialog === "search"}
        onOpenChange={(open) => setDialog(open ? "search" : null)}
      />
      {dialog === "quickAdd" ? <QuickAddTask onClose={() => setDialog(null)} /> : null}
      {dialog === "help" ? <ShortcutsHelp onClose={() => setDialog(null)} /> : null}
    </div>
  );
}
