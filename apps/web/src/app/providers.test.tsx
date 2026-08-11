import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as apiClientModule from "@/lib/api-client";
import { Providers, useTheme } from "@/app/providers";

vi.mock("@/lib/api-client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api-client")>();
  return { ...actual, apiClient: { ...actual.apiClient, get: vi.fn() } };
});

/** Controllable stand-in for `window.matchMedia("(prefers-color-scheme: dark)")`: lets a test flip
 * the OS preference and fire the same "change" event the real MediaQueryList would. */
function createMatchMediaMock(initialMatches: boolean) {
  let matches = initialMatches;
  let listener: ((event: MediaQueryListEvent) => void) | null = null;
  return {
    get matches() {
      return matches;
    },
    media: "(prefers-color-scheme: dark)",
    addEventListener: (_type: string, callback: (event: MediaQueryListEvent) => void) => {
      listener = callback;
    },
    removeEventListener: () => {
      listener = null;
    },
    dispatch(next: boolean) {
      matches = next;
      listener?.({ matches: next } as MediaQueryListEvent);
    },
  };
}

function ThemeProbe() {
  const { theme, resolvedTheme, setTheme } = useTheme();
  return (
    <div>
      <div data-testid="theme">{theme}</div>
      <div data-testid="resolved">{resolvedTheme}</div>
      <button onClick={() => setTheme("system")}>use-system</button>
    </div>
  );
}

describe("Providers theme context", () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(apiClientModule.apiClient.get).mockImplementation(() => Promise.resolve([]));
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ json: async () => ({ user: null }) }));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    document.documentElement.classList.remove("dark");
  });

  it("live-updates resolvedTheme when the OS preference changes while 'system' is active", async () => {
    const mediaQuery = createMatchMediaMock(false);
    vi.stubGlobal("matchMedia", vi.fn().mockReturnValue(mediaQuery));
    const user = userEvent.setup();

    render(
      <Providers>
        <ThemeProbe />
      </Providers>,
    );

    await user.click(screen.getByRole("button", { name: "use-system" }));
    await waitFor(() => expect(screen.getByTestId("theme")).toHaveTextContent("system"));
    expect(screen.getByTestId("resolved")).toHaveTextContent("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);

    // Simulate the OS switching to dark mode — no toggle/reload involved.
    act(() => mediaQuery.dispatch(true));

    await waitFor(() => expect(screen.getByTestId("resolved")).toHaveTextContent("dark"));
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });
});
