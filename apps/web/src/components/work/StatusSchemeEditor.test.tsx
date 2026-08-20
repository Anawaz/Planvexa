import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RemoveStatusDialog } from "./StatusSchemeEditor";
import type { StatusScheme } from "@/lib/work/types";

const removeStatusMock = vi.fn<(schemeId: string, statusId: string, moveTasksToStatusId: string) => Promise<StatusScheme>>();

vi.mock("@/lib/work/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/work/client")>();
  return {
    ...actual,
    removeStatus: (schemeId: string, statusId: string, moveTasksToStatusId: string) =>
      removeStatusMock(schemeId, statusId, moveTasksToStatusId),
  };
});

const scheme: StatusScheme = {
  id: "scheme-1",
  name: "Default",
  isDefault: true,
  spaceId: null,
  statuses: [
    { id: "todo", name: "To Do", category: "NotStarted", color: "#8b8b8b", position: 1, allowedNextStatusIds: [] },
    { id: "review", name: "In Review", category: "Active", color: "#a855f7", position: 2, allowedNextStatusIds: [] },
    { id: "done", name: "Complete", category: "Done", color: "#12b76a", position: 3, allowedNextStatusIds: [] },
  ],
};

function renderDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <RemoveStatusDialog
        scheme={scheme}
        status={scheme.statuses[1]}
        onClose={() => {}}
        onRemoved={() => {}}
      />
    </QueryClientProvider>,
  );
  return {
    select: screen.getByLabelText("Replacement status"),
    confirm: screen.getByRole("button", { name: "Remove and move tasks" }),
  };
}

describe("RemoveStatusDialog", () => {
  beforeEach(() => removeStatusMock.mockReset());

  it("cannot confirm without a replacement status", async () => {
    const user = userEvent.setup();
    const { select, confirm } = renderDialog();

    // Prefilled with the scheme's first NotStarted status, so the common case is one click.
    expect(select).toHaveValue("todo");
    expect(confirm).toBeEnabled();

    await user.selectOptions(select, "");
    expect(confirm).toBeDisabled();
    expect(removeStatusMock).not.toHaveBeenCalled();
  });

  it("sends the selected status as moveTasksToStatusId", async () => {
    removeStatusMock.mockResolvedValue(scheme);
    const user = userEvent.setup();
    const { select, confirm } = renderDialog();

    await user.selectOptions(select, "done");
    await user.click(confirm);

    expect(removeStatusMock).toHaveBeenCalledWith("scheme-1", "review", "done");
  });
});
