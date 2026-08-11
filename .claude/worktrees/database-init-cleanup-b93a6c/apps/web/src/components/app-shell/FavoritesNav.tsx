"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { recentItemHref, recentItemLabel } from "@/lib/recent/format";
import { listFavorites } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";

/**
 * A unified favourites nav section. WorkFavorite's resource_type is free-form (see
 * work.work_favorites — no CHECK constraint), covering Space/Folder/List/SavedView already and now
 * Task/Document/Dashboard/ChatChannel/Form too, with no backend change needed. Href/label building is
 * shared with RecentItemsNav (lib/recent/format) since both store the same free-form resource_type.
 */
export function FavoritesNav() {
  const favoritesQuery = useQuery({ queryKey: workKeys.favorites(), queryFn: listFavorites });
  const favorites = favoritesQuery.data ?? [];

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
            <span className="min-w-0 flex-1 truncate">{recentItemLabel(favorite.resourceType)}</span>
          </Link>
        ))}
      </div>
    </div>
  );
}
