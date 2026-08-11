import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MyWorkPageClient } from "./MyWorkPageClient";
import type { MyWorkPreferences, StatusScheme, Task } from "@/lib/work/types";

const allWorkspaces = [
  { id: "ws-1", name: "Workspace 1", slug: "workspace-1", status: "Active", role: "Owner" },
  { id: "ws-2", name: "Workspace 2", slug: "workspace-2", status: "Active", role: "Member" },
];
let mockWorkspaces = [allWorkspaces[0]];
let currentWorkspace = allWorkspaces[0];

const listMyTasksMock = vi.fn<(workspaceId?: string) => Promise<Task[]>>();
const listTasksCreatedByMeMock = vi.fn<(workspaceId?: string) => Promise<Task[]>>();
const listTasksWatchingMock = vi.fn<(workspaceId?: string) => Promise<Task[]>>();
const listStatusSchemesMock = vi.fn<() => Promise<StatusScheme[]>>();
const getMyWorkPreferencesMock = vi.fn<() => Promise<MyWorkPreferences>>();
const saveMyWorkPreferencesMock = vi.fn<(preferences: MyWorkPreferences) => Promise<MyWorkPreferences>>();

vi.mock("@/lib/work/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/work/client")>();
  return {
    ...actual,
    listMyTasks: (workspaceId?: string) => listMyTasksMock(workspaceId),
    listTasksCreatedByMe: (workspaceId?: string) => listTasksCreatedByMeMock(workspaceId),
    listTasksWatching: (workspaceId?: string) => listTasksWatchingMock(workspaceId),
    listStatusSchemes: () => listStatusSchemesMock(),
    getMyWorkPreferences: () => getMyWorkPreferencesMock(),
    saveMyWorkPreferences: (preferences: MyWorkPreferences) => saveMyWorkPreferencesMock(preferences),
  };
});

vi.mock("@/lib/members", () => ({
  useMemberDirectory: () => ({
    getLabel: (userId: string) => userId,
    getInitials: (userId: string) => userId.slice(0, 2).toUpperCase(),
    getAvatarUrl: () => null,
  }),
  // TaskDetailPanel (always mounted, gated on `open` only after its hooks run) calls these too.
  useMembers: () => ({ data: [] }),
  useTeams: () => ({ data: [] }),
  useCurrentUserId: () => "user-1",
}));

vi.mock("next/navigation", () => ({
  usePathname: () => "/app/my-work",
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
}));

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaces: mockWorkspaces, currentWorkspace }),
}));

function baseTask(overrides: Partial<Task>): Task {
  return {
    id: "task-1",
    listId: "list-1",
    spaceId: "space-1",
    sequence: "1",
    title: "Untitled",
    statusId: "todo",
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
    ...overrides,
  };
}

const scheme: StatusScheme = {
  id: "scheme-1",
  name: "Default",
  statuses: [{ id: "todo", name: "To Do", category: "NotStarted", color: "#8b8b8b", position: 1, allowedNextStatusIds: [] }],
};

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MyWorkPageClient />
    </QueryClientProvider>,
  );
}

describe("MyWorkPageClient", () => {
  beforeEach(() => {
    listMyTasksMock.mockReset();
    listTasksCreatedByMeMock.mockReset();
    listTasksWatchingMock.mockReset();
    listStatusSchemesMock.mockReset();
    getMyWorkPreferencesMock.mockReset();
    saveMyWorkPreferencesMock.mockReset();
    listStatusSchemesMock.mockResolvedValue([scheme]);
    listTasksWatchingMock.mockResolvedValue([]);
    getMyWorkPreferencesMock.mockResolvedValue({ sortBy: "dueDate", hiddenSections: [] });
    mockWorkspaces = [allWorkspaces[0]];
    currentWorkspace = allWorkspaces[0];
  });

  it("shows tasks created by the user even when none are assigned", async () => {
    listMyTasksMock.mockResolvedValue([]);
    listTasksCreatedByMeMock.mockResolvedValue([
      baseTask({ id: "created-1", title: "Unassigned but mine" }),
    ]);
    renderPage();

    await waitFor(() => expect(screen.getByText("Unassigned but mine")).toBeInTheDocument());
    // The "assigned to me" empty state still renders since no task is assigned.
    expect(screen.getByText("Nothing assigned to you yet")).toBeInTheDocument();
  });

  it("shows an empty message when the user has created nothing", async () => {
    listMyTasksMock.mockResolvedValue([]);
    listTasksCreatedByMeMock.mockResolvedValue([]);
    renderPage();

    await waitFor(() =>
      expect(screen.getByText("You haven't created any tasks yet.")).toBeInTheDocument(),
    );
  });

  it("shows tasks the user is watching", async () => {
    listMyTasksMock.mockResolvedValue([]);
    listTasksCreatedByMeMock.mockResolvedValue([]);
    listTasksWatchingMock.mockResolvedValue([
      baseTask({ id: "watched-1", title: "Watching this one" }),
    ]);
    renderPage();

    await waitFor(() => expect(screen.getByText("Watching this one")).toBeInTheDocument());
  });

  it("shows an empty message when the user is watching nothing", async () => {
    listMyTasksMock.mockResolvedValue([]);
    listTasksCreatedByMeMock.mockResolvedValue([]);
    listTasksWatchingMock.mockResolvedValue([]);
    renderPage();

    await waitFor(() =>
      expect(screen.getByText("You aren't watching any tasks yet.")).toBeInTheDocument(),
    );
  });

  it("hides the workspace filter for a member of only one workspace", async () => {
    listMyTasksMock.mockResolvedValue([]);
    listTasksCreatedByMeMock.mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(listMyTasksMock).toHaveBeenCalledWith("ws-1"));
    expect(screen.queryByLabelText("Filter My Work by workspace")).not.toBeInTheDocument();
  });

  it("defaults to the current workspace and lets a multi-workspace member switch to another one", async () => {
    mockWorkspaces = allWorkspaces;
    listMyTasksMock.mockResolvedValue([]);
    listTasksCreatedByMeMock.mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(listMyTasksMock).toHaveBeenCalledWith("ws-1"));

    const select = screen.getByLabelText("Filter My Work by workspace");
    fireEvent.change(select, { target: { value: "ws-2" } });

    await waitFor(() => expect(listMyTasksMock).toHaveBeenCalledWith("ws-2"));
    expect(listTasksCreatedByMeMock).toHaveBeenCalledWith("ws-2");
    expect(listTasksWatchingMock).toHaveBeenCalledWith("ws-2");
  });

  it("sorts assigned tasks by title when the saved preference says so", async () => {
    getMyWorkPreferencesMock.mockResolvedValue({ sortBy: "title", hiddenSections: [] });
    listMyTasksMock.mockResolvedValue([
      baseTask({ id: "b", title: "Bravo" }),
      baseTask({ id: "a", title: "Alpha" }),
    ]);
    listTasksCreatedByMeMock.mockResolvedValue([]);
    renderPage();

    const titles = await waitFor(() => {
      const found = screen.getAllByRole("button", { name: /Alpha|Bravo/ });
      expect(found).toHaveLength(2);
      return found.map((el) => el.textContent);
    });
    expect(titles).toEqual(["Alpha", "Bravo"]);
  });

  it("saves a new sort choice", async () => {
    listMyTasksMock.mockResolvedValue([]);
    listTasksCreatedByMeMock.mockResolvedValue([]);
    saveMyWorkPreferencesMock.mockResolvedValue({ sortBy: "priority", hiddenSections: [] });
    renderPage();

    await waitFor(() => expect(screen.getByLabelText("Sort My Work")).toBeInTheDocument());
    fireEvent.change(screen.getByLabelText("Sort My Work"), { target: { value: "priority" } });

    await waitFor(() =>
      expect(saveMyWorkPreferencesMock).toHaveBeenCalledWith({ sortBy: "priority", hiddenSections: [] }),
    );
  });

  it("hides the Created by me section when the saved preference hides it", async () => {
    getMyWorkPreferencesMock.mockResolvedValue({ sortBy: "dueDate", hiddenSections: ["created"] });
    listMyTasksMock.mockResolvedValue([]);
    listTasksCreatedByMeMock.mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(screen.getByText("Nothing assigned to you yet")).toBeInTheDocument());
    expect(screen.queryByRole("heading", { name: "Created by me" })).not.toBeInTheDocument();
  });

  it("unhiding a section saves the updated preference", async () => {
    getMyWorkPreferencesMock.mockResolvedValue({ sortBy: "dueDate", hiddenSections: ["watching"] });
    listMyTasksMock.mockResolvedValue([]);
    listTasksCreatedByMeMock.mockResolvedValue([]);
    saveMyWorkPreferencesMock.mockResolvedValue({ sortBy: "dueDate", hiddenSections: [] });
    renderPage();

    const watchingCheckbox = await waitFor(() => screen.getByLabelText("Watching") as HTMLInputElement);
    expect(watchingCheckbox.checked).toBe(false);
    fireEvent.click(watchingCheckbox);

    await waitFor(() =>
      expect(saveMyWorkPreferencesMock).toHaveBeenCalledWith({ sortBy: "dueDate", hiddenSections: [] }),
    );
  });
});
