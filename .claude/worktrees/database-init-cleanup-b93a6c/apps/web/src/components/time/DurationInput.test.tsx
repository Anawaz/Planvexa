import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { DurationInput } from "./DurationInput";

function Controlled({ onChange }: { onChange: (text: string, seconds: number | null) => void }) {
  const [text, setText] = useState("");
  return (
    <DurationInput
      value={text}
      onChange={(next, seconds) => {
        setText(next);
        onChange(next, seconds);
      }}
    />
  );
}

describe("DurationInput", () => {
  it("emits seconds as the text is typed", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Controlled onChange={onChange} />);

    await user.type(screen.getByLabelText("Duration"), "90");

    expect(onChange).toHaveBeenLastCalledWith("90", 5400);
  });

  it("shows the parsed duration and stays valid", () => {
    render(<DurationInput value="1h45m44s" onChange={vi.fn()} />);

    const input = screen.getByLabelText("Duration");
    expect(input).toHaveAccessibleDescription("= 1h 45m 44s");
    expect(input).not.toHaveAttribute("aria-invalid");
  });

  it("announces an invalid duration", () => {
    render(<DurationInput value="1x" onChange={vi.fn()} />);

    expect(screen.getByLabelText("Duration")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("alert")).toBeInTheDocument();
  });

  it("emits seconds from a preset chip", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<DurationInput value="" onChange={onChange} />);

    await user.click(screen.getByRole("button", { name: "1h 30m" }));

    expect(onChange).toHaveBeenCalledWith("1h 30m", 5400);
  });

  it("offers the canonical form for loose input", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<DurationInput value="2h30" onChange={onChange} />);

    await user.click(screen.getByRole("button", { name: "Use 2h 30m" }));

    expect(onChange).toHaveBeenCalledWith("2h 30m", 9000);
  });
});
