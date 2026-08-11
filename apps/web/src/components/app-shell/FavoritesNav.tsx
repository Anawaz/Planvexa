"use client";

import Link from "next/link";
import { useMemo } from "react";
import { useQueries, useQuery } from "@tanstack/react-query";
import { recentItemHref, recentItemLabel } from "@/lib/recent/format";
import { listFavorites, listLists, listSpaces } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import type { WorkFavorite } from "@/lib/work/types";

/**
 * A unified favourites nav section. WorkFavorite's resource_type is free-form (see
 * work.work_favorites — no CHECK constraint), covering Space/Folder/List/SavedView already and now
 * Task/Document/Dashboard/ChatChannel/Form too, with no backend change needed. Href/label building is
 * shared with RecentItemsNav (lib/recent/format) since both store the same free-form resource_type.
 */
export function FavoritesNav() {
  const favoritesQuery = useQuery({ queryKey: workKeys.favorites(), queryFn: listFavorites });
  const favorites = favoritesQuery.data ?? [];

  // Space/List favourites are the common case and worth a real name — everything else still falls
  // back to the generic type label (recentItemLabel) below. Neither favourite/recent-item response
  // carries a resolved name (see lib/recent/format.ts), so it's looked up client-side here from data
  // the sidebar already fetches (spaces) or would otherwise fetch per-space anyway (lists).
  const hasListFavorite = favorites.some((f) => f.resourceType.toLowerCase() === "list");
  const spacesQuery = useQuery({ queryKey: workKeys.spaces(), queryFn: listSpaces });
  const spaces = spacesQuery.data ?? [];
  const listQueries = useQueries({
    queries: spaces.map((space) => ({
      queryKey: workKeys.lists(space.id),
      queryFn: () => listLists(space.id),
      enabled: hasListFavorite,
    })),
  });

  const spaceNameById = useMemo(() => new Map(spaces.map((space) => [space.id, space.name])), [spaces]);
  const listNameById = useMemo(() => {
    const map = new Map<string, string>();
    listQueries.forEach((query) => (query.data ?? []).forEach((list) => map.set(list.id, list.name)));
    return map;
  }, [listQueries]);

  function labelFor(favorite: WorkFavorite) {
    switch (favorite.resourceType.toLowerCase()) {
      case "space":
        return spaceNameById.get(favorite.resourceId) ?? recentItemLabel(favorite.resourceType);
      case "list":
        return listNameById.get(favorite.resourceId) ?? recentItemLabel(favorite.resourceType);
      default:
        return recentItemLabel(favorite.resourceType);
    }
  }

  if (favorites.length === 0) {
    return null;
  }

  return (
    <div>
      <h2 className="px-2 pb-1 pt-4 text-[0.6875rem] font-semibold uppercase tracking-wider text-muted-foreground">
        Favourites
      </h2>
      <div className="space-y-0.5">
        {favorites.slice(0, 8).map((favorite) => (
          <Link
            key={favorite.id}
            href={recentItemHref(favorite.resourceType, favorite.resourceId)}
            className="flex items-center gap-2.5 rounded-lg px-2 py-2 text-sm font-medium text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
          >
            <span aria-hidden="true" className="shrink-0 text-amber-500">
              ★
            </span>
            <span className="min-w-0 flex-1 truncate">{labelFor(favorite)}</span>
          </Link>
        ))}
      </div>
    </div>
  );
}
