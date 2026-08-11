import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AiAssistPanel } from "./AiAssistPanel";

const summarizeTaskMock = vi.fn();
const suggestSubtasksMock = vi.fn();
const suggestPriorityMock = vi.fn();
const getAiUsageMock = vi.fn();
const getAiFeatureStatusMock = vi.fn();
const listMyTasksMock = vi.fn();
const getTaskMock = vi.fn();
const createTaskOfflineMock = vi.fn();

vi.mock("@/lib/ai/client", () => ({
  summarizeTask: (taskId: string) => summarizeTaskMock(taskId),
  suggestSubtasks: (taskId: string) => suggestSubtasksMock(taskId),
  suggestPriority: (taskId: string) => suggestPriorityMock(taskId),
  getAiUsage: () => getAiUsageMock(),
  getAiFeatureStatus: () => getAiFeatureStatusMock(),
}));

vi.mock("@/lib/work/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/work/client")>();
  return {
    ...actual,
    listMyTasks: () => listMyTasksMock(),
    getTask: (id: string) => getTaskMock(id),
  };
});

vi.mock("@/lib/work/offlineMutations", () => ({
  createTaskOffline: (input: unknown) => createTaskOfflineMock(input),
}));

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <AiAssistPanel />
    </QueryClientProvider>,
  );
}

describe("AiAssistPanel subtask suggestions", () => {
  beforeEach(() => {
    summarizeTaskMock.mockReset();
    suggestSubtasksMock.mockReset().mockResolvedValue({
      titles: ["Write tests", "Update docs"],
      tokensEstimated: 42,
    });
    suggestPriorityMock.mockReset();
    getAiFeatureStatusMock.mockReset().mockResolvedValue({ enabled: true });
    getAiUsageMock.mockReset().mockResolvedValue({
      requestCount: 1,
      tokensEstimated: 42,
      creditsEnabled: true,
      creditLimit: null,
    });
    listMyTasksMock.mockReset().mockResolvedValue([
      { id: "task-1", listId: "list-1", title: "Ship feature" },
    ]);
    getTaskMock.mockReset().mockResolvedValue({ id: "task-1", listId: "list-1", title: "Ship feature" });
    createTaskOfflineMock.mockReset().mockResolvedValue({ id: "new-task" });
  });

  it("Add selected creates one subtask per checked suggestion under the current task", async () => {
    const user = userEvent.setup();
    renderPanel();

    await screen.findByText("Ship feature");
    await user.selectOptions(screen.getByLabelText("Task"), "task-1");
    await user.click(screen.getByRole("button", { name: "Suggest subtasks" }));

    await waitFor(() => expect(screen.getByText("Write tests")).toBeInTheDocument());

    await user.click(screen.getByRole("checkbox", { name: /Write tests/ }));
    await user.click(screen.getByRole("checkbox", { name: /Update docs/ }));
    await user.click(screen.getByRole("button", { name: /Add selected/ }));

    await waitFor(() => expect(createTaskOfflineMock).toHaveBeenCalledTimes(2));
    expect(createTaskOfflineMock).toHaveBeenCalledWith({
      listId: "list-1",
      parentId: "task-1",
      title: "Write tests",
    });
    expect(createTaskOfflineMock).toHaveBeenCalledWith({
      listId: "list-1",
      parentId: "task-1",
      title: "Update docs",
    });

    await waitFor(() => expect(screen.getByText(/Added 2 subtasks/)).toBeInTheDocument());
  });
});
