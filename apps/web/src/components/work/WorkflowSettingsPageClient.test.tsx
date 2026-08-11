import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { WorkflowSettingsPageClient } from "./WorkflowSettingsPageClient";
import type { StatusScheme } from "@/lib/work/types";

const listStatusSchemesMock = vi.fn<() => Promise<StatusScheme[]>>();
const setStatusTransitionsMock = vi.fn<(schemeId: string, statusId: string, toStatusIds: string[]) => Promise<StatusScheme>>();

vi.mock("@/lib/work/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/work/client")>();
  return {
    ...actual,
    listStatusSchemes: () => listStatusSchemesMock(),
    setStatusTransitions: (schemeId: string, statusId: string, toStatusIds: string[]) =>
      setStatusTransitionsMock(schemeId, statusId, toStatusIds),
  };
});

const scheme: StatusScheme = {
  id: "scheme-1",
  name: "Default",
  statuses: [
    { id: "todo", name: "To Do", category: "NotStarted", color: "#8b8b8b", position: 1, allowedNextStatusIds: [] },
    { id: "doing", name: "In Progress", category: "Active", color: "#2b7fff", position: 2, allowedNextStatusIds: [] },
    { id: "done", name: "Complete", category: "Done", color: "#12b76a", position: 3, allowedNextStatusIds: [] },
  ],
};

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <WorkflowSettingsPageClient />
    </QueryClientProvider>,
  );
}

describe("WorkflowSettingsPageClient", () => {
  beforeEach(() => {
    listStatusSchemesMock.mockReset();
    setStatusTransitionsMock.mockReset();
  });

  it("shows every status as unrestricted by default", async () => {
    listStatusSchemesMock.mockResolvedValue([scheme]);
    renderPage();

    await waitFor(() => expect(screen.getByText("To Do")).toBeInTheDocument());
    expect(screen.getAllByText("Can move to any status in this workflow.")).toHaveLength(3);
  });

  it("turning on a restriction starts from every other status allowed, not a dead end", async () => {
    listStatusSchemesMock.mockResolvedValue([scheme]);
    setStatusTransitionsMock.mockResolvedValue(scheme);
    const user = userEvent.setup();
    renderPage();

    await waitFor(() => expect(screen.getByText("To Do")).toBeInTheDocument());
    const todoRestrictToggle = screen.getAllByRole("checkbox", { name: "Restrict where this can go" })[0];
    await user.click(todoRestrictToggle);

    expect(setStatusTransitionsMock).toHaveBeenCalledWith("scheme-1", "todo", ["doing", "done"]);
  });

  it("unchecking a specific target removes only that one from the allowed list", async () => {
    const restrictedScheme: StatusScheme = {
      ...scheme,
      statuses: [
        { ...scheme.statuses[0], allowedNextStatusIds: ["doing", "done"] },
        scheme.statuses[1],
        scheme.statuses[2],
      ],
    };
    listStatusSchemesMock.mockResolvedValue([restrictedScheme]);
    setStatusTransitionsMock.mockResolvedValue(restrictedScheme);
    const user = userEvent.setup();
    renderPage();

    await waitFor(() => expect(screen.getByRole("checkbox", { name: "Complete" })).toBeInTheDocument());
    await user.click(screen.getByRole("checkbox", { name: "Complete" }));

    expect(setStatusTransitionsMock).toHaveBeenCalledWith("scheme-1", "todo", ["doing"]);
  });
});
