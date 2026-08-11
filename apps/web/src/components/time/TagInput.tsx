"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { KeyboardEvent, useState } from "react";
import { createTimeTag, listTimeTags } from "@/lib/time/client";
import { timeKeys } from "@/lib/time/queries";
import type { TimeTag } from "@/lib/time/types";

/**
 * Free-form tag picker for a time entry: pick an existing workspace tag or type a new name and press
 * Enter/comma to create it (`createTimeTag` is idempotent by name, so a duplicate name just resolves
 * to the existing tag). Selected tags render as removable chips.
 */
export function TagInput({
  selected,
  onChange,
}: {
  selected: TimeTag[];
  onChange: (tags: TimeTag[]) => void;
}) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState("");
  const tagsQuery = useQuery({ queryKey: timeKeys.tags(), queryFn: listTimeTags });
  const createMutation = useMutation({
    mutationFn: createTimeTag,
    onSuccess: (tag) => {
      void queryClient.invalidateQueries({ queryKey: timeKeys.tags() });
      if (!selected.some((t) => t.id === tag.id)) {
        onChange([...selected, tag]);
      }
    },
  });

  const existingByName = new Map((tagsQuery.data ?? []).map((tag) => [tag.name.toLowerCase(), tag]));
  const suggestions = (tagsQuery.data ?? []).filter(
    (tag) => !selected.some((t) => t.id === tag.id) && tag.name.toLowerCase().includes(draft.trim().toLowerCase()),
  );

  function addByName(name: string) {
    const trimmed = name.trim();
    if (!trimmed) {
      return;
    }

    const existing = existingByName.get(trimmed.toLowerCase());
    if (existing) {
      if (!selected.some((t) => t.id === existing.id)) {
        onChange([...selected, existing]);
      }
    } else {
      createMutation.mutate(trimmed);
    }

    setDraft("");
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter" || event.key === ",") {
      event.preventDefault();
      addByName(draft);
    }
  }

  function remove(tagId: string) {
    onChange(selected.filter((t) => t.id !== tagId));
  }

  return (
    <div className="grid gap-1 text-xs font-medium">
      Tags
      <div className="flex flex-wrap items-center gap-1.5 rounded-lg border border-border bg-background p-1.5">
        {selected.map((tag) => (
          <span
            key={tag.id}
            className="inline-flex items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground"
          >
            {tag.name}
            <button
              type="button"
              aria-label={`Remove tag ${tag.name}`}
              className="text-muted-foreground hover:text-foreground"
              onClick={() => remove(tag.id)}
            >
              ×
            </button>
          </span>
        ))}
        <input
          type="text"
          value={draft}
          placeholder="Add a tag…"
          list="time-tag-suggestions"
          className="min-w-24 flex-1 bg-transparent px-1 py-1 text-sm font-normal outline-none"
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={handleKeyDown}
          onBlur={() => addByName(draft)}
        />
        <datalist id="time-tag-suggestions">
          {suggestions.map((tag) => (
            <option key={tag.id} value={tag.name} />
          ))}
        </datalist>
      </div>
    </div>
  );
}
