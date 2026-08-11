import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { FormSettingsPanel, reorderFields } from "./FormsPageClient";
import type { Form as CollabForm, FormFieldDef } from "@/lib/collab/types";

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

vi.mock("@/lib/members", () => ({
  useTeams: () => ({ data: [{ id: "team-1", name: "Platform Team" }], isPending: false }),
  useMembers: () => ({ data: [{ userId: "user-1", displayName: "Ada Lovelace", status: "Active" }], isPending: false }),
}));

function form(overrides: Partial<CollabForm> = {}): CollabForm {
  return {
    id: "form-1",
    listId: "list-1",
    title: "Intake",
    isActive: true,
    publicToken: "token-1",
    fields: [],
    targetTags: [],
    ...overrides,
  };
}

function renderPanel(overrides: Partial<CollabForm> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <FormSettingsPanel form={form(overrides)} />
    </QueryClientProvider>,
  );
}

describe("FormSettingsPanel team routing", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("uses a team picker populated from the workspace's teams, not a raw id text input", () => {
    renderPanel({ targetTeamId: "team-1" });

    const teamSelect = screen.getByLabelText("Team") as HTMLSelectElement;
    expect(teamSelect.tagName).toBe("SELECT");
    expect(screen.getByRole("option", { name: "Platform Team" })).toBeInTheDocument();
    expect(teamSelect.value).toBe("team-1");
    expect(screen.queryByPlaceholderText("Team UUID")).not.toBeInTheDocument();
  });
});

function field(id: string, position: number): FormFieldDef {
  return { id, label: id, type: "Text", required: false, options: [], position };
}

describe("reorderFields", () => {
  it("moves the dragged field to the drop target's index and renumbers positions", () => {
    const fields = [field("a", 1), field("b", 2), field("c", 3)];

    const result = reorderFields(fields, "a", "c");

    expect(result.map((f) => f.id)).toEqual(["b", "c", "a"]);
    expect(result.map((f) => f.position)).toEqual([1, 2, 3]);
  });

  it("moving a field onto itself is a no-op", () => {
    const fields = [field("a", 1), field("b", 2)];

    expect(reorderFields(fields, "a", "a")).toBe(fields);
  });

  it("an unknown active or over id is a no-op", () => {
    const fields = [field("a", 1), field("b", 2)];

    expect(reorderFields(fields, "missing", "b")).toBe(fields);
    expect(reorderFields(fields, "a", "missing")).toBe(fields);
  });
});
