"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/Button";
import {
  archiveList,
  archiveSpace,
  createCustomField,
  createFolder,
  createFromTemplate,
  createList,
  createSpace,
  deleteFolder,
  deleteList,
  deleteSpace,
  duplicateFolder,
  duplicateList,
  listFavorites,
  listFolders,
  listLists,
  listSpaces,
  listTemplates,
  moveFolder,
  renameFolder,
  restoreList,
  restoreSpace,
  saveAsTemplate,
  toggleFavorite,
  updateList,
  updateSpace,
} from "@/lib/work/client";
import { useWorkAction } from "@/lib/work/mutations";
import { workKeys } from "@/lib/work/queries";
import { setResourcePrivate } from "@/lib/work/sharing";
import { groupListsByFolder, type FolderTreeEntry } from "@/lib/work/structure";
import type { Folder, Space, TaskList, WorkTemplateResourceType } from "@/lib/work/types";
import { cn } from "@/lib/utils";
import { InlineComposer } from "./InlineComposer";
import { ResourceSharingDialog } from "./ResourceSharingDialog";

type Run = (call: () => Promise<unknown>) => void;

// ponytail: window.confirm is the whole destructive-action guard; swap for a dialog if design asks.
function confirmDelete(what: string) {
  return window.confirm(`Delete ${what}? Archiving keeps the data — deleting does not.`);
}

function RowMenu({ label, children }: { label: string; children: ReactNode }) {
  return (
    <details className="relative shrink-0">
      <summary
        aria-label={label}
        className="grid size-8 cursor-pointer list-none place-items-center rounded-lg text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring [&::-webkit-details-marker]:hidden"
      >
        <span aria-hidden="true">···</span>
      </summary>
      <div
        role="menu"
        className="absolute right-0 z-20 mt-1 w-52 rounded-lg border border-border bg-card p-1 text-sm shadow-xl"
      >
        {children}
      </div>
    </details>
  );
}

function MenuItem({
  onSelect,
  danger,
  children,
}: {
  onSelect: () => void;
  danger?: boolean;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      role="menuitem"
      onClick={(event) => {
        event.currentTarget.closest("details")?.removeAttribute("open");
        onSelect();
      }}
      className={cn(
        "w-full rounded-md px-2 py-2 text-left hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring",
        danger && "text-red-600 dark:text-red-400",
      )}
    >
      {children}
    </button>
  );
}

/**  . uc(a) star toggle backed by GET/POST /api/v1/favorites — works for space/folder/list rows alike. */
function FavoriteStar({
  resourceType,
  resourceId,
  run,
  pending,
}: {
  resourceType: "space" | "folder" | "list";
  resourceId: string;
  run: Run;
  pending: boolean;
}) {
  const favoritesQuery = useQuery({ queryKey: workKeys.favorites(), queryFn: listFavorites });
  const isFavorited = (favoritesQuery.data ?? []).some(
    (f) => f.resourceType === resourceType && f.resourceId === resourceId,
  );

  return (
    <button
      type="button"
      aria-pressed={isFavorited}
      aria-label={isFavorited ? "Remove from favourites" : "Add to favourites"}
      disabled={pending}
      className={cn(
        "grid size-7 shrink-0 place-items-center rounded-md text-muted-foreground hover:bg-muted hover:text-foreground disabled:opacity-50",
        isFavorited && "text-amber-500 hover:text-amber-500",
      )}
      onClick={() => run(() => toggleFavorite(resourceType, resourceId))}
    >
      <span aria-hidden="true">{isFavorited ? "★" : "☆"}</span>
    </button>
  );
}

/**  . uc(")New from template" — only renders once a template of the right resource type exists. */
function TemplatePicker({
  resourceType,
  run,
  pending,
  onDone,
  spaceId,
  folderId,
}: {
  resourceType: WorkTemplateResourceType;
  run: Run;
  pending: boolean;
  onDone: () => void;
  spaceId?: string;
  folderId?: string;
}) {
  const templatesQuery = useQuery({ queryKey: workKeys.templates(), queryFn: listTemplates });
  const [templateId, setTemplateId] = useState("");
  const [name, setName] = useState("");
  const options = (templatesQuery.data ?? []).filter((t) => t.resourceType === resourceType);

  if (options.length === 0) {
    return null;
  }

  return (
    <form
      className="mt-2 flex flex-wrap items-center gap-2 rounded-lg border border-dashed border-border p-2"
      onSubmit={(event) => {
        event.preventDefault();
        if (!templateId || !name.trim()) {
          return;
        }

        run(() => createFromTemplate(templateId, { name: name.trim(), spaceId, folderId }));
        setTemplateId("");
        setName("");
        onDone();
      }}
    >
      <select
        aria-label="Template"
        className="h-8 rounded-md border border-border bg-background px-2 text-xs"
        value={templateId}
        onChange={(event) => setTemplateId(event.currentTarget.value)}
      >
        <option value="">From template…</option>
        {options.map((template) => (
          <option key={template.id} value={template.id}>
            {template.name}
          </option>
        ))}
      </select>
      <input
        aria-label="New name"
        placeholder="Name"
        className="h-8 w-40 rounded-md border border-border bg-background px-2 text-xs"
        value={name}
        onChange={(event) => setName(event.currentTarget.value)}
      />
      <Button type="submit" size="sm" variant="secondary" disabled={pending || !templateId || !name.trim()}>
        Create
      </Button>
    </form>
  );
}

/** Flattens the folder tree (indented by depth) for the "Move to…" target picker. */
function flattenFolders(entries: FolderTreeEntry[], depth = 0): Array<{ folder: Folder; depth: number }> {
  return entries.flatMap((entry) => [
    { folder: entry.folder, depth },
    ...flattenFolders(entry.subfolders, depth + 1),
  ]);
}

function ListRow({ list, run, pending }: { list: TaskList; run: Run; pending: boolean }) {
  const [renaming, setRenaming] = useState(false);
  const [sharing, setSharing] = useState(false);

  if (renaming) {
    return (
      <InlineComposer
        className="px-2 py-1"
        label={`Rename ${list.name}`}
        submitLabel="Save"
        pending={pending}
        onSubmit={(name) => {
          run(() => updateList(list.id, { name }));
          setRenaming(false);
        }}
      />
    );
  }

  return (
    <div className="flex items-center gap-1 rounded-lg pr-1 hover:bg-muted">
      <FavoriteStar resourceType="list" resourceId={list.id} run={run} pending={pending} />
      <Link
        href={`/app/lists/${list.id}`}
        className="min-w-0 flex-1 truncate rounded-lg px-3 py-2 text-sm text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        {list.name}
        {list.isArchived ? (
          <span className="ml-2 rounded-full bg-muted px-2 py-0.5 text-xs">Archived</span>
        ) : null}
        {list.isPrivate ? (
          <span className="ml-2 rounded-full bg-muted px-2 py-0.5 text-xs">Private</span>
        ) : null}
      </Link>
      <RowMenu label={`Actions for ${list.name}`}>
        <MenuItem onSelect={() => setRenaming(true)}>Rename</MenuItem>
        <MenuItem onSelect={() => run(() => duplicateList(list.id))}>Duplicate</MenuItem>
        <MenuItem
          onSelect={() => {
            const name = window.prompt("Template name", `${list.name} template`);
            if (name?.trim()) {
              run(() => saveAsTemplate("List", list.id, name.trim()));
            }
          }}
        >
          Save as template
        </MenuItem>
        <MenuItem onSelect={() => setSharing(true)}>Share…</MenuItem>
        <MenuItem onSelect={() => run(() => setResourcePrivate("list", list.id, !list.isPrivate))}>
          {list.isPrivate ? "Make public" : "Make private"}
        </MenuItem>
        {list.isArchived ? (
          <MenuItem onSelect={() => run(() => restoreList(list.id))}>Restore</MenuItem>
        ) : (
          <MenuItem onSelect={() => run(() => archiveList(list.id))}>Archive</MenuItem>
        )}
        <MenuItem
          danger
          onSelect={() => {
            if (confirmDelete(`the list "${list.name}"`)) {
              run(() => deleteList(list.id));
            }
          }}
        >
          Delete
        </MenuItem>
      </RowMenu>
      <ResourceSharingDialog
        resourceType="list"
        resourceId={list.id}
        resourceName={list.name}
        open={sharing}
        onOpenChange={setSharing}
      />
    </div>
  );
}

/**  . uc(o)ne folder node, rendered recursively for its subfolders — arbitrary depth. */
function FolderNode({
  entry,
  depth,
  space,
  allFolders,
  run,
  pending,
}: {
  entry: FolderTreeEntry;
  depth: number;
  space: Space;
  allFolders: Folder[];
  run: Run;
  pending: boolean;
}) {
  const { folder, lists, subfolders } = entry;
  const [renaming, setRenaming] = useState(false);
  const [addingSubfolder, setAddingSubfolder] = useState(false);
  const [addingList, setAddingList] = useState(false);
  const [moving, setMoving] = useState(false);
  const [addingField, setAddingField] = useState(false);
  const [sharing, setSharing] = useState(false);
  const [showTemplates, setShowTemplates] = useState(false);

  const moveTargets = flattenFolders(
    groupListsByFolder(allFolders, []).folders,
  ).filter((candidate) => candidate.folder.id !== folder.id);

  return (
    <details open className={cn("relative rounded-xl border bg-background", depth === 0 ? "border-border" : "border-border/60 bg-card")}>
      <summary className="cursor-pointer px-4 py-3 pr-12 text-sm font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
        <span className="truncate">
          {folder.name}
          {folder.isPrivate ? (
            <span className="ml-2 rounded-full bg-muted px-2 py-0.5 text-xs font-normal">Private</span>
          ) : null}
        </span>
      </summary>
      <div className="absolute right-9 top-2">
        <FavoriteStar resourceType="folder" resourceId={folder.id} run={run} pending={pending} />
      </div>
      <div className="absolute right-2 top-2">
        <RowMenu label={`Actions for ${folder.name}`}>
          <MenuItem onSelect={() => setRenaming(true)}>Rename</MenuItem>
          <MenuItem onSelect={() => setAddingSubfolder(true)}>New subfolder</MenuItem>
          <MenuItem onSelect={() => setAddingList(true)}>New list</MenuItem>
          <MenuItem onSelect={() => setMoving((value) => !value)}>Move to…</MenuItem>
          <MenuItem onSelect={() => run(() => duplicateFolder(folder.id))}>Duplicate</MenuItem>
          <MenuItem onSelect={() => setAddingField(true)}>Add custom field</MenuItem>
          <MenuItem onSelect={() => setShowTemplates((value) => !value)}>New from template</MenuItem>
          <MenuItem
            onSelect={() => {
              const name = window.prompt("Template name", `${folder.name} template`);
              if (name?.trim()) {
                run(() => saveAsTemplate("Folder", folder.id, name.trim()));
              }
            }}
          >
            Save as template
          </MenuItem>
          <MenuItem onSelect={() => setSharing(true)}>Share…</MenuItem>
          <MenuItem onSelect={() => run(() => setResourcePrivate("folder", folder.id, !folder.isPrivate))}>
            {folder.isPrivate ? "Make public" : "Make private"}
          </MenuItem>
          <MenuItem
            danger
            onSelect={() => {
              if (confirmDelete(`the folder "${folder.name}"`)) {
                run(() => deleteFolder(folder.id));
              }
            }}
          >
            Delete
          </MenuItem>
        </RowMenu>
      </div>
      <ResourceSharingDialog
        resourceType="folder"
        resourceId={folder.id}
        resourceName={folder.name}
        open={sharing}
        onOpenChange={setSharing}
      />
      <div className="space-y-1 border-t border-border/60 p-2" style={{ marginLeft: depth > 0 ? 12 : 0 }}>
        {renaming ? (
          <InlineComposer
            label="Folder name"
            submitLabel="Save"
            pending={pending}
            onSubmit={(value) => {
              run(() => renameFolder(folder.id, value));
              setRenaming(false);
            }}
          />
        ) : null}

        {moving ? (
          <div className="flex flex-wrap items-center gap-2 rounded-lg border border-dashed border-border p-2 text-xs">
            <span className="text-muted-foreground">Move under:</span>
            <select
              aria-label="New parent folder"
              className="h-8 rounded-md border border-border bg-background px-2"
              defaultValue=""
              onChange={(event) => {
                const value = event.currentTarget.value;
                run(() => moveFolder(folder.id, value === "__top__" ? null : value));
                setMoving(false);
              }}
            >
              <option value="" disabled>
                Choose a folder…
              </option>
              <option value="__top__">Top level (no parent)</option>
              {moveTargets.map(({ folder: target, depth: targetDepth }) => (
                <option key={target.id} value={target.id}>
                  {"— ".repeat(targetDepth)}
                  {target.name}
                </option>
              ))}
            </select>
          </div>
        ) : null}

        {addingField ? (
          <InlineComposer
            label="Custom field name (applies to every list in this folder)"
            submitLabel="Add"
            pending={pending}
            onSubmit={(value) => {
              run(() => createCustomField({ name: value, type: "Text", scope: "Folder", scopeId: folder.id, isRequired: false }));
              setAddingField(false);
            }}
          />
        ) : null}

        {showTemplates ? (
          <TemplatePicker
            resourceType="Folder"
            run={run}
            pending={pending}
            spaceId={space.id}
            folderId={folder.id}
            onDone={() => setShowTemplates(false)}
          />
        ) : null}

        {lists.length > 0 ? (
          lists.map((list) => <ListRow key={list.id} list={list} run={run} pending={pending} />)
        ) : subfolders.length === 0 && !addingList ? (
          <p className="px-3 py-2 text-sm text-muted-foreground">No lists in this folder.</p>
        ) : null}

        {addingList ? (
          <InlineComposer
            label="List name"
            submitLabel="Add"
            pending={pending}
            onSubmit={(value) => {
              run(() => createList({ spaceId: space.id, folderId: folder.id, name: value }));
              setAddingList(false);
            }}
          />
        ) : null}

        {subfolders.map((sub) => (
          <FolderNode key={sub.folder.id} entry={sub} depth={depth + 1} space={space} allFolders={allFolders} run={run} pending={pending} />
        ))}

        {addingSubfolder ? (
          <InlineComposer
            label="Subfolder name"
            submitLabel="Add"
            pending={pending}
            onSubmit={(value) => {
              run(() => createFolder(space.id, value, folder.id));
              setAddingSubfolder(false);
            }}
          />
        ) : null}
      </div>
    </details>
  );
}

function SpaceCard({ space, run, pending }: { space: Space; run: Run; pending: boolean }) {
  const router = useRouter();
  const [composer, setComposer] = useState<"rename" | "folder" | "list" | null>(null);
  const [sharing, setSharing] = useState(false);
  const [showFolderTemplates, setShowFolderTemplates] = useState(false);
  const [showListTemplates, setShowListTemplates] = useState(false);
  const foldersQuery = useQuery({
    queryKey: workKeys.folders(space.id),
    queryFn: () => listFolders(space.id),
  });
  const listsQuery = useQuery({
    queryKey: workKeys.lists(space.id),
    queryFn: () => listLists(space.id),
  });
  const allFolders = foldersQuery.data ?? [];
  const { folders, ungrouped } = groupListsByFolder(allFolders, listsQuery.data ?? []);
  const isEmpty = folders.length === 0 && ungrouped.length === 0;

  function close() {
    setComposer(null);
  }

  return (
    <article
      className={cn(
        "rounded-[var(--radius)] border border-border bg-card p-5 shadow-sm",
        space.isArchived && "opacity-70",
      )}
    >
      <div className="flex items-start gap-4">
        <span
          className="relative grid size-12 shrink-0 place-items-center overflow-hidden rounded-2xl text-sm font-bold text-white shadow-sm"
          style={{ backgroundColor: space.color ?? "#2563eb" }}
          aria-hidden="true"
        >
          <span className="absolute inset-0 bg-black/20 mix-blend-multiply" />
          <span className="relative drop-shadow-sm">{space.icon ?? space.name.slice(0, 2)}</span>
        </span>
        <div className="min-w-0 flex-1">
          <h2 className="flex items-center text-lg font-semibold">
            <FavoriteStar resourceType="space" resourceId={space.id} run={run} pending={pending} />
            {space.name}
            {space.isArchived ? (
              <span className="ml-2 rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                Archived
              </span>
            ) : null}
            {space.isPrivate ? (
              <span className="ml-2 rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                Private
              </span>
            ) : null}
          </h2>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">{space.description}</p>
        </div>
        <RowMenu label={`Actions for ${space.name}`}>
          <MenuItem onSelect={() => setComposer("rename")}>Rename</MenuItem>
          <MenuItem onSelect={() => setComposer("folder")}>New folder</MenuItem>
          <MenuItem onSelect={() => setShowFolderTemplates((value) => !value)}>New folder from template</MenuItem>
          <MenuItem onSelect={() => setComposer("list")}>New list</MenuItem>
          <MenuItem onSelect={() => setShowListTemplates((value) => !value)}>New list from template</MenuItem>
          <MenuItem
            onSelect={() => {
              const name = window.prompt("Template name", `${space.name} template`);
              if (name?.trim()) {
                run(() => saveAsTemplate("Space", space.id, name.trim()));
              }
            }}
          >
            Save as template
          </MenuItem>
          <MenuItem onSelect={() => router.push(`/app/spaces/${space.id}/statuses`)}>Statuses &amp; workflow</MenuItem>
          <MenuItem onSelect={() => setSharing(true)}>Share…</MenuItem>
          <MenuItem onSelect={() => run(() => setResourcePrivate("space", space.id, !space.isPrivate))}>
            {space.isPrivate ? "Make public" : "Make private"}
          </MenuItem>
          {space.isArchived ? (
            <MenuItem onSelect={() => run(() => restoreSpace(space.id))}>Restore</MenuItem>
          ) : (
            <MenuItem onSelect={() => run(() => archiveSpace(space.id))}>Archive</MenuItem>
          )}
          <MenuItem
            danger
            onSelect={() => {
              if (confirmDelete(`the space "${space.name}" and everything in it`)) {
                run(() => deleteSpace(space.id));
              }
            }}
          >
            Delete
          </MenuItem>
        </RowMenu>
      </div>
      <ResourceSharingDialog
        resourceType="space"
        resourceId={space.id}
        resourceName={space.name}
        open={sharing}
        onOpenChange={setSharing}
      />

      {composer ? (
        <InlineComposer
          className="mt-4"
          label={
            composer === "rename"
              ? `Rename ${space.name}`
              : composer === "folder"
                ? "Folder name"
                : "List name"
          }
          submitLabel={composer === "rename" ? "Save" : "Add"}
          pending={pending}
          onSubmit={(value) => {
            if (composer === "rename") {
              run(() => updateSpace(space.id, { name: value }));
            } else if (composer === "folder") {
              run(() => createFolder(space.id, value));
            } else {
              run(() => createList({ spaceId: space.id, name: value }));
            }

            close();
          }}
        />
      ) : null}

      {showFolderTemplates ? (
        <TemplatePicker
          resourceType="Folder"
          run={run}
          pending={pending}
          spaceId={space.id}
          onDone={() => setShowFolderTemplates(false)}
        />
      ) : null}

      {showListTemplates ? (
        <TemplatePicker
          resourceType="List"
          run={run}
          pending={pending}
          spaceId={space.id}
          onDone={() => setShowListTemplates(false)}
        />
      ) : null}

      <div className="mt-4 space-y-3">
        {folders.map((entry) => (
          <FolderNode
            key={entry.folder.id}
            entry={entry}
            depth={0}
            space={space}
            allFolders={allFolders}
            run={run}
            pending={pending}
          />
        ))}

        {ungrouped.length > 0 ? (
          <div className="rounded-xl border border-border bg-background p-2">
            {ungrouped.map((list) => (
              <ListRow key={list.id} list={list} run={run} pending={pending} />
            ))}
          </div>
        ) : null}

        {isEmpty && !listsQuery.isLoading ? (
          <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
            <p>No lists in this space yet. Lists are where tasks live.</p>
            <Button
              className="mt-3"
              size="sm"
              variant="secondary"
              disabled={pending}
              onClick={() => setComposer("list")}
            >
              Create a list
            </Button>
          </div>
        ) : null}
      </div>
    </article>
  );
}

function FavoritesBar({ run, pending }: { run: Run; pending: boolean }) {
  const favoritesQuery = useQuery({ queryKey: workKeys.favorites(), queryFn: listFavorites });
  const favorites = favoritesQuery.data ?? [];

  if (favorites.length === 0) {
    return null;
  }

  return (
    <section aria-label="Favourites" className="rounded-[var(--radius)] border border-border bg-card p-4">
      <h2 className="text-sm font-semibold text-muted-foreground">Favourites</h2>
      <ul className="mt-2 flex flex-wrap gap-2">
        {favorites.map((favorite) => (
          <li key={favorite.id}>
            <button
              type="button"
              disabled={pending}
              className="inline-flex items-center gap-1 rounded-full border border-border bg-background px-3 py-1 text-xs hover:bg-muted disabled:opacity-50"
              onClick={() => run(() => toggleFavorite(favorite.resourceType, favorite.resourceId))}
            >
              <span aria-hidden="true" className="text-amber-500">★</span>
              {favorite.resourceType} · {favorite.resourceId.slice(0, 8)}
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}

export function SpacesPageClient() {
  const [creating, setCreating] = useState(false);
  const [showSpaceTemplates, setShowSpaceTemplates] = useState(false);
  const action = useWorkAction();
  const spacesQuery = useQuery({ queryKey: workKeys.spaces(), queryFn: listSpaces });
  const spaces = spacesQuery.data ?? [];

  const run: Run = (call) => action.mutate(call);

  return (
    <section aria-labelledby="spaces-title" className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium text-primary">Workspace hierarchy</p>
          <h1 id="spaces-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Spaces
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Spaces hold folders and lists; lists hold tasks. Links open the shared
            List/Table/Board work views.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" disabled={action.isPending} onClick={() => setShowSpaceTemplates((v) => !v)}>
            New from template
          </Button>
          <Button disabled={action.isPending} onClick={() => setCreating(true)}>
            New space
          </Button>
        </div>
      </div>

      <FavoritesBar run={run} pending={action.isPending} />

      {creating ? (
        <InlineComposer
          className="max-w-md"
          label="Space name"
          pending={action.isPending}
          onSubmit={(name) => {
            action.mutate(() => createSpace({ name }));
            setCreating(false);
          }}
        />
      ) : null}

      {showSpaceTemplates ? (
        <TemplatePicker resourceType="Space" run={run} pending={action.isPending} onDone={() => setShowSpaceTemplates(false)} />
      ) : null}

      {action.isError ? (
        <p role="alert" className="rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700 dark:bg-red-950 dark:text-red-200">
          That change could not be saved: {action.error.message}
        </p>
      ) : null}

      {spacesQuery.isLoading ? (
        <div className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading spaces…
        </div>
      ) : spaces.length === 0 ? (
        <div className="rounded-[var(--radius)] border border-dashed border-border bg-card p-10 text-center">
          <h2 className="text-lg font-semibold">No spaces yet</h2>
          <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-muted-foreground">
            A space is the top of the hierarchy — think “Engineering” or “Marketing”. Add one, then
            create lists inside it to start capturing tasks.
          </p>
          <Button className="mt-5" disabled={action.isPending} onClick={() => setCreating(true)}>
            Create your first space
          </Button>
        </div>
      ) : (
        <div className="grid gap-4 xl:grid-cols-2">
          {spaces.map((space) => (
            <SpaceCard key={space.id} space={space} run={run} pending={action.isPending} />
          ))}
        </div>
      )}
    </section>
  );
}
