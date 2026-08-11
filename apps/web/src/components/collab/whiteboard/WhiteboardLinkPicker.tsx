"use client";

import { Button } from "@/components/ui/Button";
import { ResourcePicker } from "@/components/ui/ResourcePicker";
import type { SearchResultType } from "@/lib/search/client";

export type LinkPickerState = { resourceType: "task" | "document"; resourceId: string; label: string };

/**
 * The "Link task/doc" toolbar flyout: type select + ResourcePicker, replacing the raw-UUID
 * window.prompt flow. Split out from WhiteboardCanvas.tsx (which pulls in react-konva/Stage — a
 * canvas element jsdom can't render without the native `canvas` package) so this interactive bit
 * stays unit-testable on its own.
 */
export function WhiteboardLinkPicker({
  state,
  onChange,
  onConfirm,
  onCancel,
}: {
  state: LinkPickerState;
  onChange: (next: LinkPickerState) => void;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <div className="flex flex-wrap items-center gap-2 rounded-[var(--radius)] border border-border bg-card p-2">
      <select
        value={state.resourceType}
        onChange={(event) => onChange({ resourceType: event.target.value as "task" | "document", resourceId: "", label: "" })}
        className="h-9 rounded-lg border border-border bg-background px-2 text-sm"
      >
        <option value="task">Task</option>
        <option value="document">Document</option>
      </select>
      <div className="w-64">
        <ResourcePicker
          types={[(state.resourceType === "task" ? "Task" : "Document") as SearchResultType]}
          value={state.resourceId}
          onChange={(id, title) => onChange({ ...state, resourceId: id, label: title })}
          placeholder={`Search ${state.resourceType}s…`}
        />
      </div>
      <Button type="button" size="sm" disabled={!state.resourceId} onClick={onConfirm}>
        Insert
      </Button>
      <Button type="button" size="sm" variant="outline" onClick={onCancel}>
        Cancel
      </Button>
    </div>
  );
}
