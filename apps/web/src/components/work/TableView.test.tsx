import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TableView } from "./TableView";
import { useTaskSelection } from "./selection";
import type { StatusDefinition, Task } from "@/lib/work/types";

vi.mock("@/lib/members", () => ({
  useMemberDirectory: () => ({
    getLabel: (userId: string) => userId,
    getInitials: (userId: string) => userId.slice(0, 2).toUpperCase(),
    getAvatarUrl: () => null,
  }),
}));

const statuses: StatusDefinition[] = [
  { id: "status-1", name: "To Do", category: "NotStarted", color: "#64748b", position: 0, allowedNextStatusIds: [] },
];

function task(overrides: Partial<Task>): Task {
  return {
    id: overrides.id ?? "task-1",
    listId: "list-1",
    spaceId: "space-1",
    sequence: "1",
    title: "Task",
    statusId: "status-1",
    priority: "Normal",
    dueDate: undefined,
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

function Wrapper({ tasks }: { tasks: Task[] }) {
  const selection = useTaskSelection();
  return (
    <TableView tasks={tasks} statuses={statuses} selection={selection} onOpenTask={() => {}} />
  );
}

describe("TableView", () => {
  it("reflects the active sort column/direction via aria-sort", async () => {
    const user = userEvent.setup();
    const tasks = [
      task({ id: "task-1", title: "Bravo", dueDate: "2026-01-02" }),
      task({ id: "task-2", title: "Alpha", dueDate: "2026-01-01" }),
    ];
    render(<Wrapper tasks={tasks} />);

    // Default sort is dueDate ascending (see TableView's initial `sorting` state).
    expect(screen.getByRole("columnheader", { name: /due date/i })).toHaveAttribute(
      "aria-sort",
      "ascending",
    );
    expect(screen.getByRole("columnheader", { name: /^title/i })).toHaveAttribute("aria-sort", "none");

    // Sortable columns with no sort applied still declare aria-sort="none" (not just omit it).
    const titleButton = screen.getByRole("button", { name: /title/i });
    await user.click(titleButton);

    expect(screen.getByRole("columnheader", { name: /^title/i })).toHaveAttribute(
      "aria-sort",
      "ascending",
    );
    expect(screen.getByRole("columnheader", { name: /due date/i })).toHaveAttribute(
      "aria-sort",
      "none",
    );

    await user.click(titleButton);
    expect(screen.getByRole("columnheader", { name: /^title/i })).toHaveAttribute(
      "aria-sort",
      "descending",
    );
  });

  it("does not set aria-sort on a non-sortable column", () => {
    render(<Wrapper tasks={[task({})]} />);
    expect(screen.getByRole("columnheader", { name: /select/i })).not.toHaveAttribute("aria-sort");
  });
});
