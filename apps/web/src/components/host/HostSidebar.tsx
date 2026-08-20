"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Icon, type IconName } from "@/components/app-shell/icons";
import { Wordmark } from "@/components/app-shell/Wordmark";
import { cn } from "@/lib/utils";

/**
 * The host console's navigation, built to the same spec as the workspace shell's
 * <c>Sidebar</c>/<c>SidebarNavigation</c> — same 18rem rail, same card background, same section
 * headings, same icon + active-pill link treatment — so moving between the two consoles feels like one
 * product rather than two.
 *
 * It is a separate component rather than a reuse of `SidebarNavigation` because that one is entirely
 * workspace-scoped: it renders the space tree, favourites and an unread badge, all of which query
 * workspace-scoped endpoints that a host request has no workspace for. Sharing the LOOK without
 * sharing the DATA is the point.
 */
type HostNavItem = {
  href: string;
  label: string;
  icon: IconName;
  /** Match the path exactly — the console root would otherwise be "active" on every page. */
  exact?: boolean;
  description: string;
};

type HostNavSection = { label: string; items: HostNavItem[] };

export const hostNavSections: HostNavSection[] = [
  {
    label: "Instance",
    items: [
      { href: "/host", label: "Overview", icon: "dashboards", exact: true, description: "Counts, activity and trends" },
      { href: "/host/workspaces", label: "Workspaces", icon: "space", description: "Every workspace on this server" },
      { href: "/host/users", label: "Accounts", icon: "members", description: "Every registered account" },
    ],
  },
  {
    label: "Operations",
    items: [
      { href: "/host/activity", label: "Activity", icon: "activity", description: "Instance-wide audit trail" },
      { href: "/host/logs", label: "Logs", icon: "list", description: "Warnings and errors" },
      { href: "/host/health", label: "Health", icon: "check", description: "Database, outbox, configuration" },
    ],
  },
  {
    label: "Configuration",
    items: [
      { href: "/host/settings", label: "Settings", icon: "settings", description: "Access, branding and support" },
    ],
  },
];

export function isHostNavItemActive(pathname: string, item: HostNavItem) {
  return item.exact ? pathname === item.href : pathname.startsWith(item.href);
}

const sectionHeadingClass =
  "px-2 pb-1 pt-4 text-[0.6875rem] font-semibold uppercase tracking-wider text-muted-foreground";

function HostNavLink({
  item,
  active,
  onNavigate,
}: {
  item: HostNavItem;
  active: boolean;
  onNavigate?: () => void;
}) {
  return (
    <Link
      href={item.href}
      aria-current={active ? "page" : undefined}
      onClick={onNavigate}
      title={item.description}
      className={cn(
        "flex items-center gap-2.5 rounded-lg px-2 py-2 text-sm font-medium transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none",
        active
          ? "bg-primary text-primary-foreground"
          : "text-muted-foreground hover:bg-muted hover:text-foreground",
      )}
    >
      <Icon name={item.icon} className="shrink-0" />
      <span className="min-w-0 flex-1 truncate">{item.label}</span>
    </Link>
  );
}

export function HostSidebarNavigation({
  className,
  onNavigate,
}: {
  className?: string;
  onNavigate?: () => void;
}) {
  const pathname = usePathname();

  return (
    <nav className={cn("flex flex-col overflow-y-auto", className)} aria-label="Host administration">
      {hostNavSections.map((section) => (
        <div key={section.label}>
          <h2 className={sectionHeadingClass}>{section.label}</h2>
          <div className="space-y-0.5">
            {section.items.map((item) => (
              <HostNavLink
                key={item.href}
                item={item}
                active={isHostNavItemActive(pathname, item)}
                onNavigate={onNavigate}
              />
            ))}
          </div>
        </div>
      ))}

      {/* Pinned to the bottom, mirroring the workspace sidebar's Manage block — the way out of the
          console should always be in the same place, not scrolled off with the nav. */}
      <div className="mt-auto border-t border-border pt-3">
        <Link
          href="/app/my-work"
          onClick={onNavigate}
          className="flex items-center gap-2.5 rounded-lg px-2 py-2 text-sm font-medium text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none"
        >
          <Icon name="chevronRight" className="shrink-0 rotate-180" />
          <span className="min-w-0 flex-1 truncate">Back to your workspace</span>
        </Link>
      </div>
    </nav>
  );
}

/**
 * The console wordmark. Amber rather than the workspace shell's plain heading: actions taken in here
 * affect every workspace on the server, and the operator should be able to tell the two consoles apart
 * at a glance rather than by reading the URL.
 */
export function HostWordmark() {
  return (
    <Wordmark
      href="/host"
      suffix={
        <span className="shrink-0 rounded-full bg-amber-100 px-2 py-0.5 text-[0.6875rem] font-semibold uppercase tracking-wider text-amber-900 dark:bg-amber-950 dark:text-amber-100">
          Host
        </span>
      }
    />
  );
}

export function HostSidebar() {
  return (
    <aside className="hidden lg:fixed lg:inset-y-0 lg:left-0 lg:z-40 lg:flex lg:w-72 lg:flex-col lg:border-r lg:border-border lg:bg-card">
      <div className="flex h-16 items-center border-b border-border px-6">
        <HostWordmark />
      </div>
      <HostSidebarNavigation className="flex-1 px-4 pb-6 pt-2" />
    </aside>
  );
}
