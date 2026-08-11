import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RuleEditor } from "./AutomationsPageClient";
import type { AutomationRule } from "@/lib/collab/types";

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

vi.mock("@/lib/members", () => ({
  useMembers: () => ({ data: [{ userId: "user-1", displayName: "Ada Lovelace", status: "Active" }], isPending: false }),
}));

vi.mock("@/lib/work/client", () => ({
  listStatusSchemes: () =>
    Promise.resolve([
      {
        id: "scheme-1",
        name: "Default",
        statuses: [
          { id: "status-todo", name: "To Do", category: "NotStarted", color: "#8b8b8b", position: 1, allowedNextStatusIds: [] },
          { id: "status-done", name: "Complete", category: "Done", color: "#12b76a", position: 2, allowedNextStatusIds: [] },
        ],
      },
    ]),
  listTags: () => Promise.resolve([{ id: "tag-1", name: "urgent" }]),
  listSpaces: () => Promise.resolve([{ id: "space-1", name: "Engineering", position: 0, isArchived: false }]),
  listLists: () => Promise.resolve([{ id: "list-1", spaceId: "space-1", name: "Sprint 1", statusSchemeId: "scheme-1", position: 0 }]),
}));

function rule(overrides: Partial<AutomationRule> = {}): AutomationRule {
  return {
    id: "rule-1",
    name: "Notify on status change",
    triggerType: "task.status_changed",
    isEnabled: true,
    conditionJson: '{"field":"toStatusId","equals":"status-done"}',
    actionJson: '{"type":"notify","value":""}',
    ...overrides,
  };
}

function renderEditor(overrides: Partial<AutomationRule> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <RuleEditor rule={rule(overrides)} onDeleted={() => {}} />
    </QueryClientProvider>,
  );
}

describe("AutomationsPageClient condition builder", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("serializes the real WorkspaceEvent.Data keys (toStatusId/assigneeUserId/listId), not display names", () => {
    renderEditor();

    const matchField = screen.getByLabelText("Match field") as HTMLSelectElement;
    // The dropdown shows a friendly label...
    expect(screen.getByRole("option", { name: "Status changed to" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Assignee" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "List" })).toBeInTheDocument();
    // ...but the option's value — what actually gets serialized — is the real event key.
    expect(matchField.value).toBe("toStatusId");

    // The rendered conditionJson must use the real key so AutomationEngine.EvaluateNode's exact-key
    // lookup against WorkspaceEvent.Data can ever match.
    const conditionCode = screen.getByText(/"field":"toStatusId"/);
    expect(conditionCode).toBeInTheDocument();
    expect(screen.queryByText(/"field":"status"/)).not.toBeInTheDocument();
  });

  it("renders a status picker (not a raw text box) for the toStatusId condition value", async () => {
    renderEditor();

    const equalsField = (await screen.findByLabelText("Equals")) as HTMLSelectElement;
    expect(equalsField.tagName).toBe("SELECT");
    await screen.findByRole("option", { name: "Complete" });
    expect(equalsField.value).toBe("status-done");
  });

  it("switching the match field to List uses a list-select picker populated from the workspace, not a raw UUID input", async () => {
    const user = userEvent.setup();
    renderEditor({ conditionJson: '{"field":"listId","equals":"list-1"}' });

    const matchField = screen.getByLabelText("Match field") as HTMLSelectElement;
    expect(matchField.value).toBe("listId");

    const equalsField = (await screen.findByLabelText("Equals")) as HTMLSelectElement;
    expect(equalsField.tagName).toBe("SELECT");
    expect(await screen.findByRole("option", { name: "Engineering / Sprint 1" })).toBeInTheDocument();
    expect(equalsField.value).toBe("list-1");

    await user.selectOptions(matchField, "listId");
    expect(screen.queryByPlaceholderText("In review")).not.toBeInTheDocument();
  });

  it("renders a member picker (not a free-text message box) for the notify action value", async () => {
    renderEditor();

    // AutomationDispatcher.ApplyAsync's Notify case Guid.TryParse's action.Value as the recipient user
    // id — there is no message field — so the builder must use the same select-based member picker as
    // "assign", not a text input.
    const valueField = (await screen.findByLabelText("Value")) as HTMLSelectElement;
    expect(valueField.tagName).toBe("SELECT");
    expect(await screen.findByRole("option", { name: "Ada Lovelace" })).toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Notification message")).not.toBeInTheDocument();
  });
});
