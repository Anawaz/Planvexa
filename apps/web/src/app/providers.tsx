"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AppContextProvider } from "@/lib/app-context/AppContext";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  useSyncExternalStore,
  type ReactNode,
} from "react";

export type Theme = "light" | "dark" | "system";
type ResolvedTheme = "light" | "dark";

type ThemeContextValue = {
  /** The user's preference, including "system" — what the 3-way control in Topbar shows. */
  theme: Theme;
  /** "light" or "dark" — what "system" currently resolves to. This is what gets applied to <html>. */
  resolvedTheme: ResolvedTheme;
  setTheme: (theme: Theme) => void;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

function getSystemDarkSnapshot(): boolean {
  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

function getSystemDarkServerSnapshot(): boolean {
  return false;
}

/** Pre-auth/instant-paint fallback only — reconciled with the account's server-side preference once
 * it loads (see AuthenticatedAppLayout), which is the source of truth once signed in. */
function resolveInitialTheme(): Theme {
  if (typeof window === "undefined") {
    return "system";
  }

  const storedTheme = window.localStorage.getItem("planvexa-theme");
  if (storedTheme === "light" || storedTheme === "dark" || storedTheme === "system") {
    return storedTheme;
  }

  return "system";
}

export function Providers({ children }: { children: ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          // React Query's default networkMode ('online') pauses queryFn/mutationFn entirely while
          // navigator.onLine is false, so they never run -- which means the offline read-through
          // cache (lib/work/client.ts) and the IndexedDB outbox (lib/offline/withOfflineFallback.ts)
          // never get a chance to do their own, more precise online/offline handling. 'always' lets
          // every fetch attempt through so that custom logic decides, exactly as the offline/PWA
          // support (see apps/web/src/lib/offline/) is designed to.
          queries: {
            retry: 1,
            staleTime: 30_000,
            networkMode: "always",
          },
          mutations: {
            networkMode: "always",
          },
        },
      }),
  );
  const [theme, setTheme] = useState<Theme>(() => resolveInitialTheme());

  // Only subscribes to the OS "change" event while "system" is actually selected — the subscribe
  // callback returns a no-op when it isn't, so useSyncExternalStore registers no listener at all
  // (and tears down the real one, via its own cleanup, the moment the user picks an explicit
  // light/dark preference or this component unmounts).
  const subscribeToSystemTheme = useCallback(
    (onStoreChange: () => void) => {
      if (theme !== "system") {
        return () => {};
      }

      const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
      mediaQuery.addEventListener("change", onStoreChange);
      return () => mediaQuery.removeEventListener("change", onStoreChange);
    },
    [theme],
  );
  const systemDark = useSyncExternalStore(subscribeToSystemTheme, getSystemDarkSnapshot, getSystemDarkServerSnapshot);

  const resolvedTheme: ResolvedTheme = theme === "system" ? (systemDark ? "dark" : "light") : theme;

  useEffect(() => {
    document.documentElement.classList.toggle("dark", resolvedTheme === "dark");
    document.documentElement.dataset.theme = resolvedTheme;
    window.localStorage.setItem("planvexa-theme", theme);
  }, [theme, resolvedTheme]);

  const value = useMemo<ThemeContextValue>(
    () => ({ theme, resolvedTheme, setTheme }),
    [theme, resolvedTheme],
  );

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeContext.Provider value={value}><AppContextProvider>{children}</AppContextProvider></ThemeContext.Provider>
    </QueryClientProvider>
  );
}

export function useTheme() {
  const context = useContext(ThemeContext);

  if (!context) {
    throw new Error("useTheme must be used inside Providers");
  }

  return context;
}

