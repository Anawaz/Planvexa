import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { createElement } from "react";
import { useFileDropZone } from "./useFileDropZone";

function DropZone({ onFiles, disabled }: { onFiles: (files: File[]) => void; disabled?: boolean }) {
  const { isDraggingOver, dropZoneProps } = useFileDropZone(onFiles, disabled);
  return createElement("div", {
    "data-testid": "zone",
    "data-dragging": isDraggingOver,
    ...dropZoneProps,
  });
}

function fileDataTransfer(files: File[]) {
  return { types: ["Files"], files };
}

describe("useFileDropZone", () => {
  it("calls onFiles with the dropped files and prevents the default navigation", () => {
    const onFiles = vi.fn();
    const { getByTestId } = render(createElement(DropZone, { onFiles }));
    const zone = getByTestId("zone");
    const file = new File(["hello"], "hello.txt", { type: "text/plain" });

    const event = fireEvent.drop(zone, { dataTransfer: fileDataTransfer([file]) });

    expect(onFiles).toHaveBeenCalledTimes(1);
    expect(onFiles).toHaveBeenCalledWith([file]);
    expect(event).toBe(false); // fireEvent returns false when preventDefault() was called
  });

  it("tracks drag-over state across enter and leave", () => {
    const { getByTestId } = render(createElement(DropZone, { onFiles: vi.fn() }));
    const zone = getByTestId("zone");

    fireEvent.dragEnter(zone, { dataTransfer: fileDataTransfer([]) });
    expect(zone.dataset.dragging).toBe("true");

    fireEvent.dragLeave(zone, { dataTransfer: fileDataTransfer([]) });
    expect(zone.dataset.dragging).toBe("false");
  });

  it("does nothing on drop when disabled", () => {
    const onFiles = vi.fn();
    const { getByTestId } = render(createElement(DropZone, { onFiles, disabled: true }));
    const file = new File(["hello"], "hello.txt", { type: "text/plain" });

    fireEvent.drop(getByTestId("zone"), { dataTransfer: fileDataTransfer([file]) });

    expect(onFiles).not.toHaveBeenCalled();
  });

  it("ignores a drop that carries no files", () => {
    const onFiles = vi.fn();
    const { getByTestId } = render(createElement(DropZone, { onFiles }));

    fireEvent.drop(getByTestId("zone"), { dataTransfer: fileDataTransfer([]) });

    expect(onFiles).not.toHaveBeenCalled();
  });
});
