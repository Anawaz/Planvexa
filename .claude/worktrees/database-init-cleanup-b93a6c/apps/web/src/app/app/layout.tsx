"use client";

import { useRouter } from "next/navigation";
import { useCallback, useState, type ReactNode } from "react";
import { CommandPalette } from "@/components/app-shell/CommandPalette";
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
  const { workspaceId } = useAppContext();
  const router = useRouter();
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
