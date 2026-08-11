"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useMemo, useState, useSyncExternalStore } from "react";
import { useQuery } from "@tanstack/react-query";
import { InlineComposer } from "@/components/work/InlineComposer";
import {
  createFolder,
  createList,
  createSpace,
  listFolders,
  listLists,
  listSpaces,
} from "@/lib/work/client";
import { useWorkAction } from "@/lib/work/mutations";
import { workKeys } from "@/lib/work/queries";
import {
  SIDEBAR_EXPANDED_KEY,
  groupListsByFolder,
  parseExpanded,
  toggleExpanded,
} from "@/lib/work/structure";
import type { Space } from "@/lib/work/types";
import { cn } from "@/lib/utils";
import { Icon } from "./icons";

type Composer =
  | { kind: "space" }
  | { kind: "folder"; spaceId: string }
  | { kind: "list"; spaceId: string; folderId?: string }
  | null;

/** localStorage is the source of truth for expansion, so the desktop tree and the mobile drawer
 *  tree stay in step (and so do other tabs, via the native `storage` event). */
const EXPANDED_EVENT = "planvexa:sidebar-expanded";

function subscribeExpanded(onChange: () => void) {
  window.addEventListener(EXPANDED_EVENT, onChange);
  window.addEventListener("storage", onChange);

  return () => {
    window.removeEventListener(EXPANDED_EVENT, onChange);
    window.removeEventListener("storage", onChange);
  };
}

const rowClass = "group flex items-center gap-1 rounded-lg pr-1 hover:bg-muted";
const rowLabelClass =
  "flex min-w-0 flex-1 items-center gap-2 rounded-lg px-2 py-1.5 text-left text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";
const childrenClass = "ml-4 border-l border-border pl-1";

function AddButton({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      className="grid size-6 shrink-0 place-items-center rounded-md text-muted-foreground opacity-60 hover:bg-background hover:text-foreground focus-visible:opacity-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring group-hover:opacity-100"
    >
      <Icon name="plus" />
    </button>
  );
}

function Chevron({ expanded }: { expanded: boolean }) {
  return (
    <Icon
      name="chevronRight"
      className={cn(
        "shrink-0 text-muted-foreground transition-transform duration-150 motion-reduce:transition-none",
        expanded && "rotate-90",
      )}
    />
  );
}

function ListLink({
  href,
  name,
  active,
  onNavigate,
}: {
  href: string;
  name: string;
  active: boolean;
  onNavigate?: () => void;
}) {
  return (
    <Link
      href={href}
      aria-current={active ? "page" : undefined}
      onClick={onNavigate}
      className={cn(
        "ml-1 flex items-center gap-2 rounded-lg px-2 py-1.5 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
        active
          ? "bg-primary text-primary-foreground"
          : "text-muted-foreground hover:bg-muted hover:text-foreground",
      )}
    >
      <Icon name="list" className="shrink-0 opacity-70" />
      <span className="truncate">{name}</span>
    </Link>
  );
}

function SpaceNode({
  space,
  expandedIds,
  onToggle,
  composer,
  onComposerChange,
  pending,
  onCreate,
  onNavigate,
}: {
  space: Space;
  expandedIds: string[];
  onToggle: (id: string) => void;
  composer: Composer;
  onComposerChange: (composer: Composer) => void;
  pending: boolean;
  onCreate: (run: () => Promise<unknown>) => void;
  onNavigate?: () => void;
}) {
  const pathname = usePathname();
  const isExpanded = expandedIds.includes(space.id);
  // Children only load once the space is open — a big workspace must not fan out on mount.
  const foldersQuery = useQuery({
    queryKey: workKeys.folders(space.id),
    queryFn: () => listFolders(space.id),
    enabled: isExpanded,
  });
  const listsQuery = useQuery({
    queryKey: workKeys.lists(space.id),
    queryFn: () => listLists(space.id),
    enabled: isExpanded,
  });
  const { folders, ungrouped } = groupListsByFolder(foldersQuery.data ?? [], listsQuery.data ?? []);
  const isLoading = foldersQuery.isLoading || listsQuery.isLoading;

  return (
    <li>
      <div className={rowClass}>
        <button
          type="button"
          aria-expanded={isExpanded}
          onClick={() => onToggle(space.id)}
          className={cn(rowLabelClass, "font-medium text-foreground")}
        >
          <Chevron expanded={isExpanded} />
          <span
            aria-hidden="true"
            className="grid size-4 shrink-0 place-items-center rounded text-[0.625rem] font-bold text-white"
            style={{ backgroundColor: space.color ?? "#2563eb" }}
          >
            {(space.icon ?? space.name).slice(0, 1)}
          </span>
          <span className="truncate">{space.name}</span>
        </button>
        <AddButton
          label={`New list in ${space.name}`}
          onClick={() => onComposerChange({ kind: "list", spaceId: space.id })}
        />
      </div>

      {isExpanded ? (
        <div className={childrenClass}>
          {folders.map(({ folder, lists, subfolders }) => {
            const folderExpanded = expandedIds.includes(folder.id);

            return (
              <div key={folder.id}>
                <div className={rowClass}>
                  <button
                    type="button"
                    aria-expanded={folderExpanded}
                    onClick={() => onToggle(folder.id)}
                    className={cn(rowLabelClass, "text-muted-foreground hover:text-foreground")}
                  >
                    <Chevron expanded={folderExpanded} />
                    <Icon name="folder" className="shrink-0" />
                    <span className="truncate">{folder.name}</span>
                  </button>
                  <AddButton
                    label={`New list in ${folder.name}`}
                    onClick={() =>
                      onComposerChange({ kind: "list", spaceId: space.id, folderId: folder.id })
                    }
                  />
                </div>
                {folderExpanded ? (
                  <div className={childrenClass}>
                    {lists.map((list) => (
                      <ListLink
                        key={list.id}
                        href={`/app/lists/${list.id}`}
                        name={list.name}
                        active={pathname === `/app/lists/${list.id}`}
                        onNavigate={onNavigate}
                      />
                    ))}
                    {subfolders.map(({ folder: sub, lists: subLists }) => {
                      const subExpanded = expandedIds.includes(sub.id);
                      return (
                        <div key={sub.id}>
                          <div className={rowClass}>
                            <button
                              type="button"
                              aria-expanded={subExpanded}
                              onClick={() => onToggle(sub.id)}
                              className={cn(rowLabelClass, "text-muted-foreground hover:text-foreground")}
                            >
                              <Chevron expanded={subExpanded} />
                              <Icon name="folder" className="shrink-0" />
                              <span className="truncate">{sub.name}</span>
                            </button>
                            <AddButton
                              label={`New list in ${sub.name}`}
                              onClick={() =>
                                onComposerChange({ kind: "list", spaceId: space.id, folderId: sub.id })
                              }
                            />
                          </div>
                          {subExpanded ? (
                            <div className={childrenClass}>
                              {subLists.map((list) => (
                                <ListLink
                                  key={list.id}
                                  href={`/app/lists/${list.id}`}
                                  name={list.name}
                                  active={pathname === `/app/lists/${list.id}`}
                                  onNavigate={onNavigate}
                                />
                              ))}
                              {composer?.kind === "list" && composer.folderId === sub.id ? (
                                <InlineComposer
                                  className="px-2 py-1"
                                  label="List name"
                                  pending={pending}
                                  onSubmit={(name) =>
                                    onCreate(() =>
                                      createList({ spaceId: space.id, folderId: sub.id, name }),
                                    )
                                  }
                                />
                              ) : subLists.length === 0 ? (
                                <p className="px-3 py-1.5 text-xs text-muted-foreground">No lists yet</p>
                              ) : null}
                            </div>
                          ) : null}
                        </div>
                      );
                    })}
                    {composer?.kind === "list" && composer.folderId === folder.id ? (
                      <InlineComposer
                        className="px-2 py-1"
                        label="List name"
                        pending={pending}
                        onSubmit={(name) =>
                          onCreate(() =>
                            createList({ spaceId: space.id, folderId: folder.id, name }),
                          )
                        }
                      />
                    ) : lists.length === 0 ? (
                      <p className="px-3 py-1.5 text-xs text-muted-foreground">No lists yet</p>
                    ) : null}
                  </div>
                ) : null}
              </div>
            );
          })}

          {ungrouped.map((list) => (
            <ListLink
              key={list.id}
              href={`/app/lists/${list.id}`}
              name={list.name}
              active={pathname === `/app/lists/${list.id}`}
              onNavigate={onNavigate}
            />
          ))}

          {composer?.kind === "list" && composer.spaceId === space.id && !composer.folderId ? (
            <InlineComposer
              className="px-2 py-1"
              label="List name"
              pending={pending}
              onSubmit={(name) => onCreate(() => createList({ spaceId: space.id, name }))}
            />
          ) : null}

          {composer?.kind === "folder" && composer.spaceId === space.id ? (
            <InlineComposer
              className="px-2 py-1"
              label="Folder name"
              pending={pending}
              onSubmit={(name) => onCreate(() => createFolder(space.id, name))}
            />
          ) : (
            <button
              type="button"
              onClick={() => onComposerChange({ kind: "folder", spaceId: space.id })}
              className="ml-1 flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-xs text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              <Icon name="plus" className="shrink-0" />
              New folder
            </button>
          )}

          {isLoading ? (
            <p className="px-3 py-1.5 text-xs text-muted-foreground">Loading…</p>
          ) : null}
        </div>
      ) : null}
    </li>
  );
}

/** The spaces → folders → lists tree, with create affordances at every level. */
export function SidebarSpaceTree({ onNavigate }: { onNavigate?: () => void }) {
  const [composer, setComposer] = useState<Composer>(null);
  const action = useWorkAction();
  const spacesQuery = useQuery({ queryKey: workKeys.spaces(), queryFn: listSpaces });
  const spaces = (spacesQuery.data ?? []).filter((space) => !space.isArchived);
  // Server snapshot is null: the tree renders collapsed in the HTML and expands after hydration.
  const stored = useSyncExternalStore(
    subscribeExpanded,
    () => window.localStorage.getItem(SIDEBAR_EXPANDED_KEY),
    () => null,
  );
  const expandedIds = useMemo(() => parseExpanded(stored), [stored]);

  function persist(next: string[]) {
    window.localStorage.setItem(SIDEBAR_EXPANDED_KEY, JSON.stringify(next));
    window.dispatchEvent(new Event(EXPANDED_EVENT));
  }

  function toggle(id: string) {
    persist(toggleExpanded(expandedIds, id));
  }

  /** Opening a composer for a node implies opening the node itself. */
  function openComposer(next: Composer) {
    if (next && next.kind !== "space") {
      const ids = [next.spaceId, next.kind === "list" ? next.folderId : undefined].filter(
        (id): id is string => typeof id === "string" && !expandedIds.includes(id),
      );

      if (ids.length > 0) {
        persist([...expandedIds, ...ids]);
      }
    }

    setComposer(next);
  }

  function create(run: () => Promise<unknown>) {
    action.mutate(run, { onSuccess: () => setComposer(null) });
  }

  return (
    <div className="pt-4">
      <div className="flex items-center gap-1 pr-1">
        <h2 className="min-w-0 flex-1">
          <Link
            href="/app/spaces"
            onClick={onNavigate}
            className="block truncate rounded-md px-2 py-1 text-[0.6875rem] font-semibold uppercase tracking-wider text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            Spaces
          </Link>
        </h2>
        <AddButton label="New space" onClick={() => openComposer({ kind: "space" })} />
      </div>

      {composer?.kind === "space" ? (
        <InlineComposer
          className="px-2 py-1"
          label="Space name"
          pending={action.isPending}
          onSubmit={(name) => create(() => createSpace({ name }))}
        />
      ) : null}

      <ul className="space-y-0.5">
        {spaces.map((space) => (
          <SpaceNode
            key={space.id}
            space={space}
            expandedIds={expandedIds}
            onToggle={toggle}
            composer={composer}
            onComposerChange={openComposer}
            pending={action.isPending}
            onCreate={create}
            onNavigate={onNavigate}
          />
        ))}
      </ul>

      {spacesQuery.isLoading ? (
        <p className="px-3 py-1.5 text-xs text-muted-foreground">Loading spaces…</p>
      ) : spaces.length === 0 && composer?.kind !== "space" ? (
        <button
          type="button"
          onClick={() => setComposer({ kind: "space" })}
          className="flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-xs text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          <Icon name="plus" className="shrink-0" />
          Create your first space
        </button>
      ) : null}
    </div>
  );
}
