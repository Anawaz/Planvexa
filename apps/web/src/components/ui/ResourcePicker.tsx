"use client";

import { useEffect, useId, useRef, useState } from "react";
import { useSearch } from "@/lib/search/useSearch";
import type { SearchResultType } from "@/lib/search/client";

type ResourcePickerProps = {
  id?: string;
  types: SearchResultType[];
  value: string;
  onChange: (id: string, title: string) => void;
  placeholder?: string;
  disabled?: boolean;
};

/**
 * Type-to-search picker over the permission-filtered global search endpoint, restricted to `types`.
 * Resolves to the same underlying id a raw-id text field would, without ever asking the user to know
 * or paste one (spec: normal users must never enter a raw UUID/database identifier).
 */
export function ResourcePicker({ id, types, value, onChange, placeholder, disabled }: ResourcePickerProps) {
  const [query, setQuery] = useState("");
  const [selectedLabel, setSelectedLabel] = useState("");
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const listboxId = useId();
  const { data: results, isFetching } = useSearch(open ? query : "");
  const matches = (results ?? []).filter((result) => types.includes(result.type));

  // The caller resets `value` to "" after a successful create/link — follow it so a fresh picker
  // shows the search box again instead of the previous pick's stale label. Adjusted during render
  // (React's recommended pattern for resetting state from a prop change) rather than in an effect.
  const [lastValue, setLastValue] = useState(value);
  if (value !== lastValue) {
    setLastValue(value);
    if (!value) setSelectedLabel("");
  }

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  function select(resultId: string, title: string) {
    onChange(resultId, title);
    setSelectedLabel(title);
    setQuery("");
    setOpen(false);
  }

  function clear() {
    onChange("", "");
    setSelectedLabel("");
    setQuery("");
  }

  const showSelected = value && selectedLabel;

  return (
    <div ref={containerRef} className="relative">
      {showSelected ? (
        <div className="flex h-9 items-center justify-between gap-2 rounded-lg border border-border bg-background px-3 text-sm">
          <span className="truncate">{selectedLabel}</span>
          <button
            type="button"
            onClick={clear}
            disabled={disabled}
            className="shrink-0 text-xs text-muted-foreground hover:text-foreground"
          >
            Change
          </button>
        </div>
      ) : (
        <input
          id={id}
          type="text"
          role="combobox"
          aria-expanded={open}
          aria-controls={listboxId}
          aria-autocomplete="list"
          value={query}
          disabled={disabled}
          placeholder={placeholder ?? "Type to search…"}
          onChange={(event) => {
            setQuery(event.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          autoComplete="off"
          className="h-9 w-full rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        />
      )}

      {open && !showSelected && query.trim().length >= 2 ? (
        <ul id={listboxId} role="listbox" className="absolute z-10 mt-1 max-h-64 w-full overflow-auto rounded-lg border border-border bg-card py-1 text-sm shadow-lg">
          {isFetching ? (
            <li className="px-3 py-2 text-muted-foreground">Searching…</li>
          ) : matches.length === 0 ? (
            <li className="px-3 py-2 text-muted-foreground">No matches.</li>
          ) : (
            matches.map((result) => (
              <li key={`${result.type}-${result.id}`} role="option" aria-selected={false}>
                <button
                  type="button"
                  onClick={() => select(result.id, result.title)}
                  className="flex w-full flex-col items-start px-3 py-2 text-left hover:bg-muted"
                >
                  <span className="font-medium">{result.title}</span>
                  <span className="text-xs text-muted-foreground">
                    {result.type}
                    {result.subtitle ? ` · ${result.subtitle}` : ""}
                  </span>
                </button>
              </li>
            ))
          )}
        </ul>
      ) : null}
    </div>
  );
}
