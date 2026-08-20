"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { getAiFeatureStatus } from "@/lib/ai/client";
import { aiKeys } from "@/lib/ai/queries";
import { unreadCount } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import { cn } from "@/lib/utils";
import { FavoritesNav } from "./FavoritesNav";
import { Icon } from "./icons";
import { isNavItemActive, navSections, type NavItem, type NavSection } from "./nav-config";
import { SidebarSpaceTree } from "./SidebarSpaceTree";
import { Wordmark } from "./Wordmark";

// The command palette consumes this flat list; the sidebar itself renders the grouped structure.
export { navigation } from "./nav-config";

const sectionHeadingClass =
  "px-2 pb-1 pt-4 text-[0.6875rem] font-semibold uppercase tracking-wider text-muted-foreground";

function NavLink({
  item,
  active,
  badge,
  muted,
  onNavigate,
}: {
  item: NavItem;
  active: boolean;
  badge?: number;
  muted?: boolean;
  onNavigate?: () => void;
}) {
  return (
    <Link
      href={item.href}
      aria-current={active ? "page" : undefined}
      onClick={onNavigate}
      className={cn(
        "flex items-center gap-2.5 rounded-lg px-2 py-2 font-medium transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none",
        muted ? "text-xs" : "text-sm",
        active
          ? "bg-primary text-primary-foreground"
          : "text-muted-foreground hover:bg-muted hover:text-foreground",
      )}
    >
      <Icon name={item.icon} className="shrink-0" />
      <span className="min-w-0 flex-1 truncate">{item.label}</span>
      {badge ? (
        <span className="min-w-5 shrink-0 rounded-full bg-red-600 px-1.5 py-0.5 text-center text-[0.65rem] font-semibold leading-none text-white">
          {badge > 99 ? "99+" : badge}
        </span>
      ) : null}
    </Link>
  );
}

function LinkSection({
  section,
  pathname,
  unread,
  muted,
  onNavigate,
}: {
  section: NavSection;
  pathname: string;
  unread?: number;
  muted?: boolean;
  onNavigate?: () => void;
}) {
  return (
    <div>
      <h2 className={sectionHeadingClass}>{section.label}</h2>
      <div className="space-y-0.5">
        {section.items.map((item) => (
          <NavLink
            key={item.href}
            item={item}
            active={isNavItemActive(pathname, item)}
            badge={item.href === "/app/notifications" ? unread : undefined}
            muted={muted}
            onNavigate={onNavigate}
          />
        ))}
      </div>
    </div>
  );
}

type SidebarNavigationProps = {
  className?: string;
  onNavigate?: () => void;
};

export function SidebarNavigation({ className, onNavigate }: SidebarNavigationProps) {
  const pathname = usePathname();
  // Same query key as NotificationBell — React Query shares the one cache entry and its 30s poll,
  // so the badge costs no extra request.
  const unread = useQuery({ queryKey: collabKeys.unreadCount(), queryFn: unreadCount }).data ?? 0;
  const aiEnabled = useQuery({ queryKey: aiKeys.featureStatus(), queryFn: getAiFeatureStatus }).data?.enabled ?? true;
  const toolsSection: NavSection = {
    ...navSections.tools,
    items: navSections.tools.items.filter((item) => aiEnabled || item.href !== "/app/ai"),
  };

  return (
    <nav className={cn("flex flex-col overflow-y-auto", className)} aria-label="Primary app navigation">
      <LinkSection
        section={navSections.workspace}
        pathname={pathname}
        unread={unread}
        onNavigate={onNavigate}
      />
      <FavoritesNav />
      <SidebarSpaceTree onNavigate={onNavigate} />
      <LinkSection section={navSections.views} pathname={pathname} onNavigate={onNavigate} />
      <LinkSection section={toolsSection} pathname={pathname} onNavigate={onNavigate} />
      <div className="mt-auto border-t border-border pt-1">
        <LinkSection
          section={navSections.manage}
          pathname={pathname}
          muted
          onNavigate={onNavigate}
        />
      </div>
    </nav>
  );
}

export function Sidebar() {
  return (
    <aside className="hidden lg:fixed lg:inset-y-0 lg:left-0 lg:z-40 lg:flex lg:w-72 lg:flex-col lg:border-r lg:border-border lg:bg-card">
      <div className="flex h-16 items-center border-b border-border px-6">
        <Wordmark href="/app" />
      </div>
      <SidebarNavigation className="flex-1 px-4 pb-6 pt-2" />
    </aside>
  );
}
