"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { listRecentItems } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import { recentItemHref, recentItemLabel } from "@/lib/recent/format";

/**  . uc(")jump back to what you had open" — the last few resources this user viewed, any type. */
export function RecentItemsNav() {
  const recentQuery = useQuery({ queryKey: workKeys.recentItems(), queryFn: () => listRecentItems(6) });
  const items = recentQuery.data ?? [];

  if (items.length === 0) {
    return null;
  }

  return (
    <div>
      <h2 className="px-2 pb-1 pt-4 text-[0.6875rem] font-semibold uppercase tracking-wider text-muted-foreground">
        Recent
      </h2>
      <div className="space-y-0.5">
        {items.map((item) => (
          <Link
            key={`${item.resourceType}:${item.resourceId}`}
            href={recentItemHref(item.resourceType, item.resourceId)}
            className="flex items-center gap-2.5 rounded-lg px-2 py-2 text-sm font-medium text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
          >
            <span className="min-w-0 flex-1 truncate">{recentItemLabel(item.resourceType)}</span>
          </Link>
        ))}
      </div>
    </div>
  );
}
