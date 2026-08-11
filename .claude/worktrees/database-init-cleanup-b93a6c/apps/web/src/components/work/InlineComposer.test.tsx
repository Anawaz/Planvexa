import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { InlineComposer } from "./InlineComposer";

describe("InlineComposer", () => {
  it("renders the label and submit button", () => {
    render(<InlineComposer label="Add a task" onSubmit={vi.fn()} />);
    expect(screen.getByLabelText("Add a task")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add" })).toBeInTheDocument();
  });

  it("submits the trimmed title and clears the input", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<InlineComposer label="Add a task" onSubmit={onSubmit} />);

    const input = screen.getByLabelText("Add a task");
    await user.type(input, "  Ship the feature  ");
    await user.click(screen.getByRole("button", { name: "Add" }));

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledWith("Ship the feature");
    expect(input).toHaveValue("");
  });

  it("does nothing when submitting a blank title", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<InlineComposer label="Add a task" onSubmit={onSubmit} />);

    await user.type(screen.getByLabelText("Add a task"), "   ");
    await user.click(screen.getByRole("button", { name: "Add" }));

    expect(onSubmit).not.toHaveBeenCalled();
  });
});
