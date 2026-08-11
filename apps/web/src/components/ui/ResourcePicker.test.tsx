import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { ResourcePicker } from "./ResourcePicker";
import type { SearchResult } from "@/lib/search/client";

const searchMock = vi.fn<(term: string) => Promise<SearchResult[]>>();

vi.mock("@/lib/search/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/search/client")>();
  return { ...actual, search: (term: string) => searchMock(term) };
});

const results: SearchResult[] = [
  { type: "Task", id: "task-1", title: "Ship the release", subtitle: "Backlog" },
  { type: "Space", id: "space-1", title: "Engineering" },
];

function Controlled({ onChange }: { onChange: (id: string, title: string) => void }) {
  const [value, setValue] = useState("");
  return (
    <ResourcePicker
      id="picker"
      types={["Task"]}
      value={value}
      onChange={(id, title) => {
        setValue(id);
        onChange(id, title);
      }}
    />
  );
}

function renderPicker(onChange = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <Controlled onChange={onChange} />
    </QueryClientProvider>,
  );
  return { onChange };
}

describe("ResourcePicker", () => {
  beforeEach(() => {
    searchMock.mockReset();
  });

  it("only offers results matching the allowed types", async () => {
    searchMock.mockResolvedValue(results);
    const user = userEvent.setup();
    renderPicker();

    await user.type(screen.getByRole("combobox"), "ship");

    await waitFor(() => expect(screen.getByText("Ship the release")).toBeInTheDocument());
    expect(screen.queryByText("Engineering")).not.toBeInTheDocument();
  });

  it("selecting a result reports its id and title, and shows a selected chip with a way to change it", async () => {
    searchMock.mockResolvedValue(results);
    const user = userEvent.setup();
    const { onChange } = renderPicker();

    await user.type(screen.getByRole("combobox"), "ship");
    await waitFor(() => expect(screen.getByText("Ship the release")).toBeInTheDocument());
    await user.click(screen.getByText("Ship the release"));

    expect(onChange).toHaveBeenCalledWith("task-1", "Ship the release");
    expect(screen.getByText("Ship the release")).toBeInTheDocument();
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Change" }));
    expect(screen.getByRole("combobox")).toBeInTheDocument();
  });

  it("does not query until the term reaches two characters", async () => {
    searchMock.mockResolvedValue(results);
    const user = userEvent.setup();
    renderPicker();

    await user.type(screen.getByRole("combobox"), "s");

    await new Promise((resolve) => setTimeout(resolve, 300));
    expect(searchMock).not.toHaveBeenCalled();
  });
});
