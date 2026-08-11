import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { TaskDetailPanel } from "./TaskDetailPanel";
import type { StatusDefinition, TaskDetail } from "@/lib/work/types";

const getTaskMock = vi.fn<() => Promise<TaskDetail>>();
const listAttachmentsMock = vi.fn<() => Promise<[]>>();
const uploadAttachmentMock = vi.fn<(taskId: string, file: File) => Promise<unknown>>();

vi.mock("@/lib/work/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/work/client")>();
  return {
    ...actual,
    getTask: () => getTaskMock(),
    listAttachments: () => listAttachmentsMock(),
    uploadAttachment: (taskId: string, file: File) => uploadAttachmentMock(taskId, file),
  };
});

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1", currentWorkspace: { role: "Owner" } }),
}));

vi.mock("@/lib/recent/useRecordRecentView", () => ({
  useRecordRecentView: () => {},
}));

// Heavy sibling panels/editors aren't part of this test — stub them so the test stays focused on
// the Attachments drop zone (each already has its own tests/coverage elsewhere).
vi.mock("@/components/collab/tiptap/BasicRichTextEditor", () => ({
  BasicRichTextEditor: () => null,
}));
vi.mock("@/components/collab/CommentThread", () => ({ CommentThread: () => null }));
vi.mock("@/components/collab/ShareDialog", () => ({ ShareDialog: () => null }));
vi.mock("@/components/work/ResourceSharingDialog", () => ({ ResourceSharingDialog: () => null }));
vi.mock("@/components/time/TaskTimeSection", () => ({ TaskTimeSection: () => null }));

const statuses: StatusDefinition[] = [
  { id: "status-1", name: "Todo", category: "Active", color: "#000", position: 0, allowedNextStatusIds: [] },
];

function task(overrides: Partial<TaskDetail> = {}): TaskDetail {
  return {
    id: "task-1",
    listId: "list-1",
    spaceId: "space-1",
    sequence: "1",
    title: "Ship the feature",
    statusId: "status-1",
    priority: "Normal",
    isMilestone: false,
    assigneeUserIds: [],
    watcherUserIds: [],
    tagIds: [],
    position: 0,
    isCompleted: false,
    isPrivate: false,
    teamAssigneeIds: [],
    isArchived: false,
    checklists: [],
    dependencies: [],
    customFieldValues: [],
    activity: [],
    lists: [],
    relations: [],
    ...overrides,
  };
}

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <TaskDetailPanel taskId="task-1" open statuses={statuses} onClose={vi.fn()} />
    </QueryClientProvider>,
  );
}

describe("TaskDetailPanel attachment drop zone", () => {
  beforeEach(() => {
    getTaskMock.mockReset();
    listAttachmentsMock.mockReset();
    uploadAttachmentMock.mockReset();
    getTaskMock.mockResolvedValue(task());
    listAttachmentsMock.mockResolvedValue([]);
    uploadAttachmentMock.mockResolvedValue(undefined);
  });

  it("uploads a file dropped onto the attachments section, same as the file input", async () => {
    renderPanel();

    const heading = await screen.findByRole("heading", { name: "Attachments" });
    const section = heading.closest("section")!;
    const file = new File(["contents"], "notes.txt", { type: "text/plain" });

    fireEvent.drop(section, { dataTransfer: { types: ["Files"], files: [file] } });

    await waitFor(() => expect(uploadAttachmentMock).toHaveBeenCalledWith("task-1", file));
  });

  it("shows a drag-over highlight while a file is dragged over the section", async () => {
    renderPanel();

    const heading = await screen.findByRole("heading", { name: "Attachments" });
    const section = heading.closest("section")!;

    fireEvent.dragEnter(section, { dataTransfer: { types: ["Files"], files: [] } });
    expect(section.className).toContain("border-primary");

    fireEvent.dragLeave(section, { dataTransfer: { types: ["Files"], files: [] } });
    expect(section.className).not.toContain("border-primary");
  });
});
