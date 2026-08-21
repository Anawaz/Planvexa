"use client";

import { useEffect, useId, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import { recentItemHref, recentItemLabel } from "@/lib/recent/format";
import { searchResultHref } from "@/lib/search/client";
import { useSearch } from "@/lib/search/useSearch";
import { listRecentItems } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import type { Space, TaskList } from "@/lib/work/types";
import { cn } from "@/lib/utils";
import { navigation } from "./Sidebar";

const navCommands = [
  // `?new=1` opens My Work's quick-add dialog, which carries its own list picker.
  { label: "New task", hint: "Create a task", href: "/app/my-work?new=1" },
  { label: "Overview", hint: "App home", href: "/app" },
  ...navigation.map((item) => ({ label: item.label, hint: "Navigate", href: item.href })),
];

type Command = { label: string; hint: string; href: string; group?: string };

type CommandPaletteProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function CommandPalette({ open, onOpenChange }: CommandPaletteProps) {
  const [query, setQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const router = useRouter();
  const queryClient = useQueryClient();
  const titleId = useId();
  const descriptionId = useId();

  // Spaces/lists already in the cache from the shell's own queries — no extra fetch.
  const commands = useMemo<Command[]>(() => {
    if (!open) {
      return navCommands;
    }

    const spaces = (queryClient.getQueryData<Space[]>(workKeys.spaces()) ?? []).map((space) => ({
      label: space.name,
      hint: "Space",
      href: "/app/spaces",
    }));
    const lists = queryClient
      .getQueriesData<TaskList[]>({ queryKey: [...workKeys.all, "spaces"] })
      .filter(([key]) => key[3] === "lists")
      .flatMap(([, data]) => data ?? [])
      .map((list) => ({ label: list.name, hint: "List", href: `/app/lists/${list.id}` }));

    return [...navCommands, ...spaces, ...lists];
  }, [open, queryClient]);

  // Cross-module search (tasks/lists/folders/spaces/documents/comments/chat/members/teams/dashboards/
  // forms — see SearchAggregator), appended below the navigation commands.
  const { data: searchResults } = useSearch(open ? query : "");

  // Empty box: offer "jump back to what you had open" instead of the same static nav list every time.
  const recentQuery = useQuery({
    queryKey: workKeys.recentItems(),
    queryFn: () => listRecentItems(8),
    enabled: open && query.trim().length === 0,
  });

  const visibleCommands = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    const matches = normalizedQuery
      ? commands.filter((command) =>
          `${command.label} ${command.hint}`.toLowerCase().includes(normalizedQuery),
        )
      : commands;

    const results = (searchResults ?? []).map<Command>((result) => ({
      label: result.title,
      hint: result.subtitle ? `${result.type} · ${result.subtitle}` : result.type,
      href: searchResultHref(result),
      group: "Results",
    }));

    const recent = normalizedQuery
      ? []
      : (recentQuery.data ?? []).map<Command>((item) => ({
          label: recentItemLabel(item.resourceType),
          hint: "Recently viewed",
          href: recentItemHref(item.resourceType, item.resourceId),
          group: "Recent",
        }));

    return [...recent, ...matches, ...results];
  }, [commands, query, searchResults, recentQuery.data]);

  useEffect(() => {
    if (open) {
      window.setTimeout(() => inputRef.current?.focus(), 0);
    }
  }, [open]);

  const selectedCommandIndex =
    visibleCommands.length > 0 ? Math.min(selectedIndex, visibleCommands.length - 1) : -1;

  if (!open) {
    return null;
  }

  function runCommand(command: Command) {
    onOpenChange(false);
    router.push(command.href);
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto px-4 py-16 sm:px-6 sm:py-24"
      role="presentation"
    >
      <button
        type="button"
        className="absolute inset-0 cursor-default bg-slate-950/50 backdrop-blur-sm pv-animate-backdrop"
        aria-label="Close command palette"
        onClick={() => onOpenChange(false)}
      />
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        className="relative w-full max-w-2xl overflow-hidden rounded-[var(--radius)] border border-border bg-card shadow-2xl shadow-black/30 pv-animate-command"
        onKeyDown={(event) => {
          if (event.key === "Escape") {
            event.stopPropagation();
            onOpenChange(false);
            return;
          }

          if (event.key === "ArrowDown") {
            event.preventDefault();
            setSelectedIndex((index) =>
              visibleCommands.length > 0 ? Math.min(index + 1, visibleCommands.length - 1) : 0,
            );
            return;
          }

          if (event.key === "ArrowUp") {
            event.preventDefault();
            setSelectedIndex((index) => Math.max(index - 1, 0));
            return;
          }

          if (event.key === "Enter" && visibleCommands[selectedCommandIndex]) {
            event.preventDefault();
            runCommand(visibleCommands[selectedCommandIndex]);
          }
        }}
      >
        <div className="border-b border-border p-4">
          <h2 id={titleId} className="text-lg font-semibold">
            Command palette
          </h2>
          <p id={descriptionId} className="mt-1 text-sm text-muted-foreground">
            Jump to a page, or search your workspace for a task, list, or space.
          </p>
          <input
            ref={inputRef}
            value={query}
            onChange={(event) => {
              setSelectedIndex(0);
              setQuery(event.currentTarget.value);
            }}
            aria-label="Search commands"
            placeholder="Search tasks, lists, spaces — or type a command"
            className="mt-4 w-full rounded-lg border border-border bg-background px-3 py-2 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          />
        </div>
        <ul className="max-h-[min(20rem,50dvh)] overflow-y-auto p-2" aria-label="Available commands">
          {visibleCommands.length > 0 ? (
            visibleCommands.map((command, index) => (
              <li key={`${index}:${command.href}`}>
                {command.group && visibleCommands[index - 1]?.group !== command.group ? (
                  <h3 className="px-3 pb-1 pt-3 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                    {command.group}
                  </h3>
                ) : null}
                <button
                  type="button"
                  aria-current={index === selectedCommandIndex ? "true" : undefined}
                  className={cn(
                    "flex w-full items-center justify-between rounded-lg px-3 py-3 text-left text-sm hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring",
                    index === selectedCommandIndex && "bg-muted text-foreground",
                  )}
                  onMouseEnter={() => setSelectedIndex(index)}
                  onClick={() => runCommand(command)}
                >
                  <span>
                    <span className="block font-medium">{command.label}</span>
                    <span className="block text-xs text-muted-foreground">
                      {command.hint}
                    </span>
                  </span>
                  <span className="text-xs text-muted-foreground">{command.href}</span>
                </button>
              </li>
            ))
          ) : (
            <li className="px-3 py-8 text-center text-sm text-muted-foreground">
              No command or result found.
            </li>
          )}
        </ul>
        <div className="flex justify-end border-t border-border p-3">
          <Button type="button" variant="ghost" size="sm" onClick={() => onOpenChange(false)}>
            Close
          </Button>
        </div>
      </section>
    </div>
  );
}
