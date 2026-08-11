import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import * as apiClientModule from "@/lib/api-client";
import { AppContextProvider, useAppContext } from "@/lib/app-context/AppContext";

vi.mock("@/lib/api-client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api-client")>();
  return { ...actual, apiClient: { ...actual.apiClient, get: vi.fn() } };
});

const memberships = [
  { id: "ws-1", name: "WS1", slug: "ws1", status: "Active", createdAtUtc: "2026-01-01T00:00:00Z", role: "Owner" },
  { id: "ws-2", name: "WS2", slug: "ws2", status: "Active", createdAtUtc: "2026-01-01T00:00:00Z", role: "Owner" },
];

function mockApiGet() {
  vi.mocked(apiClientModule.apiClient.get).mockImplementation((path: string) => {
    if (path === "/users/me") return Promise.resolve({ userId: "u-1", email: "owner@planvexa.local", displayName: "Dev Owner" });
    if (path === "/workspaces/me") return Promise.resolve(memberships);
    if (path === "/features") return Promise.resolve([]);
    return Promise.resolve([]);
  });
}

function Probe() {
  const ctx = useAppContext();
  return (
    <div>
      <div data-testid="user">{ctx.currentUserId ?? ""}</div>
      <div data-testid="workspace">{ctx.currentWorkspace?.slug ?? ""}</div>
      <button onClick={() => ctx.setCurrentWorkspaceId("ws-2")}>switch</button>
    </div>
  );
}

function renderProvider(queryClient: QueryClient) {
  return render(
    <QueryClientProvider client={queryClient}>
      <AppContextProvider>
        <Probe />
      </AppContextProvider>
    </QueryClientProvider>,
  );
}

describe("AppContextProvider", () => {
  beforeEach(() => {
    localStorage.clear();
    mockApiGet();
    // A signed-in session: the workspaces query gates on a resolved user.
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        json: async () => ({ user: { subject: "sub-owner", email: "owner@planvexa.local", name: "Dev Owner" } }),
      }),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("resolves the default workspace from /workspaces/me and syncs the api client context", async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const setApiContextSpy = vi.spyOn(apiClientModule, "setApiContext");

    renderProvider(queryClient);

    await waitFor(() => expect(screen.getByTestId("workspace")).toHaveTextContent("ws1"));
    expect(screen.getByTestId("user")).toHaveTextContent("u-1");
    expect(setApiContextSpy).toHaveBeenCalledWith({ workspaceId: "ws-1" });
  });

  it("switching workspaces persists the selection and invalidates the query cache", async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");
    const user = userEvent.setup();

    renderProvider(queryClient);
    await waitFor(() => expect(screen.getByTestId("workspace")).toHaveTextContent("ws1"));

    await user.click(screen.getByRole("button", { name: "switch" }));

    await waitFor(() => expect(screen.getByTestId("workspace")).toHaveTextContent("ws2"));
    expect(localStorage.getItem("planvexa-active-workspace")).toBe("ws-2");
    expect(invalidateSpy).toHaveBeenCalled();
  });
});
