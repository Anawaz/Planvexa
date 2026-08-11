import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { WhiteboardLinkPicker, type LinkPickerState } from "./WhiteboardLinkPicker";
import type { SearchResult } from "@/lib/search/client";

const searchMock = vi.fn<(term: string) => Promise<SearchResult[]>>();

vi.mock("@/lib/search/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/search/client")>();
  return { ...actual, search: (term: string) => searchMock(term) };
});

const results: SearchResult[] = [
  { type: "Task", id: "task-1", title: "Ship the release" },
  { type: "Document", id: "doc-1", title: "Launch plan" },
];

function Harness({ onConfirm }: { onConfirm: (state: LinkPickerState) => void }) {
  const [state, setState] = useState<LinkPickerState>({ resourceType: "task", resourceId: "", label: "" });
  return (
    <WhiteboardLinkPicker
      state={state}
      onChange={setState}
      onConfirm={() => onConfirm(state)}
      onCancel={() => {}}
    />
  );
}

function renderPicker(onConfirm = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <Harness onConfirm={onConfirm} />
    </QueryClientProvider>,
  );
  return { onConfirm };
}

describe("WhiteboardLinkPicker", () => {
  beforeEach(() => {
    searchMock.mockReset();
  });

  it("never exposes a raw id input — only a type select and a search combobox", () => {
    renderPicker();
    const comboboxes = screen.getAllByRole("combobox");
    expect(comboboxes).toHaveLength(2);
    expect(comboboxes.some((el) => el.tagName === "SELECT")).toBe(true);
    expect(comboboxes.some((el) => el.tagName === "INPUT")).toBe(true);
    expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText(/id/i)).not.toBeInTheDocument();
  });

  it("searches only tasks by default and disables Insert until a result is picked", async () => {
    searchMock.mockResolvedValue(results);
    const user = userEvent.setup();
    renderPicker();

    expect(screen.getByRole("button", { name: "Insert" })).toBeDisabled();

    const combobox = screen.getAllByRole("combobox").find((el) => el.tagName === "INPUT")!;
    await user.type(combobox, "ship");

    await waitFor(() => expect(screen.getByText("Ship the release")).toBeInTheDocument());
    expect(screen.queryByText("Launch plan")).not.toBeInTheDocument();

    await user.click(screen.getByText("Ship the release"));
    expect(screen.getByRole("button", { name: "Insert" })).toBeEnabled();
  });

  it("switching the type to Document searches documents instead", async () => {
    searchMock.mockResolvedValue(results);
    const user = userEvent.setup();
    renderPicker();

    const select = screen.getAllByRole("combobox").find((el) => el.tagName === "SELECT")!;
    await user.selectOptions(select, "document");

    const combobox = screen.getAllByRole("combobox").find((el) => el.tagName === "INPUT")!;
    await user.type(combobox, "launch");

    await waitFor(() => expect(screen.getByText("Launch plan")).toBeInTheDocument());
    expect(screen.queryByText("Ship the release")).not.toBeInTheDocument();
  });

  it("confirms with the picked resource id and label, not a hand-typed value", async () => {
    searchMock.mockResolvedValue(results);
    const user = userEvent.setup();
    const onConfirm = vi.fn();
    renderPicker(onConfirm);

    const combobox = screen.getAllByRole("combobox").find((el) => el.tagName === "INPUT")!;
    await user.type(combobox, "ship");
    await waitFor(() => expect(screen.getByText("Ship the release")).toBeInTheDocument());
    await user.click(screen.getByText("Ship the release"));

    await user.click(screen.getByRole("button", { name: "Insert" }));

    expect(onConfirm).toHaveBeenCalledWith({ resourceType: "task", resourceId: "task-1", label: "Ship the release" });
  });
});
