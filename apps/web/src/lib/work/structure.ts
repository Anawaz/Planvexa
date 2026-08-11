import type { Folder, TaskList } from "./types";

/** Sidebar tree expansion — ids of expanded spaces and folders, persisted verbatim. */
export const SIDEBAR_EXPANDED_KEY = "planvexa-sidebar-expanded";

/** localStorage value → id list. Anything unparseable reads as "nothing expanded". */
export function parseExpanded(raw: string | null): string[] {
  try {
    const parsed = JSON.parse(raw ?? "null");
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === "string") : [];
  } catch {
    return [];
  }
}

export function toggleExpanded(ids: string[], id: string): string[] {
  return ids.includes(id) ? ids.filter((current) => current !== id) : [...ids, id];
}

function byPosition(left: { position: number }, right: { position: number }) {
  return left.position - right.position;
}

/** One folder plus its direct lists and (recursively) its subfolders, to arbitrary depth. */
export type FolderTreeEntry = { folder: Folder; lists: TaskList[]; subfolders: FolderTreeEntry[] };

/**
 * The folder tree (folders nest to arbitrary depth, not just one level) plus the lists sitting
 * directly under the space. `GET /spaces/{id}/lists` returns every list in the space, foldered or not.
 */
export function groupListsByFolder(folders: Folder[], lists: TaskList[]) {
  const sorted = [...lists].sort(byPosition);
  const listsIn = (folderId: string) => sorted.filter((list) => list.folderId === folderId);

  const byParent = new Map<string | null, Folder[]>();
  for (const folder of folders) {
    const key = folder.parentFolderId ?? null;
    const bucket = byParent.get(key);
    if (bucket) {
      bucket.push(folder);
    } else {
      byParent.set(key, [folder]);
    }
  }
  for (const bucket of byParent.values()) {
    bucket.sort(byPosition);
  }

  function buildTree(parentId: string | null): FolderTreeEntry[] {
    return (byParent.get(parentId) ?? []).map((folder) => ({
      folder,
      lists: listsIn(folder.id),
      subfolders: buildTree(folder.id),
    }));
  }

  return {
    folders: buildTree(null),
    ungrouped: sorted.filter((list) => !list.folderId),
  };
}
