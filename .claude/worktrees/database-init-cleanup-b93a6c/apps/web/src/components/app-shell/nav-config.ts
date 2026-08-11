import type { IconName } from "./icons";

export type NavItem = {
  href: string;
  label: string;
  icon: IconName;
  activePrefixes?: string[];
};

export type NavSection = { label: string; items: NavItem[] };

/**
 * The sidebar's link sections. The Spaces tree renders between `workspace` and `views`;
 * `manage` is the low-emphasis footer block.
 */
export const navSections = {
  workspace: {
    label: "Workspace",
    items: [
      { href: "/app/my-work", label: "My Work", icon: "check", activePrefixes: ["/app/my-work"] },
      { href: "/app/inbox", label: "Inbox", icon: "inbox", activePrefixes: ["/app/inbox"] },
      {
        href: "/app/notifications",
        label: "Notifications",
        icon: "bell",
        activePrefixes: ["/app/notifications"],
      },
    ],
  },
  views: {
    label: "Views",
    items: [
      { href: "/app/calendar", label: "Calendar", icon: "calendar", activePrefixes: ["/app/calendar"] },
      { href: "/app/gantt", label: "Gantt", icon: "gantt", activePrefixes: ["/app/gantt"] },
      { href: "/app/timeline", label: "Timeline", icon: "timeline", activePrefixes: ["/app/timeline"] },
      { href: "/app/workload", label: "Workload", icon: "workload", activePrefixes: ["/app/workload"] },
      { href: "/app/team", label: "Team", icon: "team", activePrefixes: ["/app/team"] },
      { href: "/app/sprints", label: "Sprints", icon: "sprints", activePrefixes: ["/app/sprints"] },
      { href: "/app/goals", label: "Goals", icon: "goals", activePrefixes: ["/app/goals"] },
      { href: "/app/activity", label: "Activity", icon: "activity", activePrefixes: ["/app/activity"] },
      { href: "/app/map", label: "Map", icon: "map", activePrefixes: ["/app/map"] },
      {
        href: "/app/dashboards",
        label: "Dashboards",
        icon: "dashboards",
        activePrefixes: ["/app/dashboards"],
      },
    ],
  },
  tools: {
    label: "Tools",
    items: [
      { href: "/app/chat", label: "Chat", icon: "chat", activePrefixes: ["/app/chat"] },
      { href: "/app/documents", label: "Documents", icon: "documents", activePrefixes: ["/app/documents"] },
      { href: "/app/whiteboards", label: "Whiteboards", icon: "whiteboard", activePrefixes: ["/app/whiteboards"] },
      { href: "/app/clips", label: "Clips", icon: "clips", activePrefixes: ["/app/clips"] },
      { href: "/app/forms", label: "Forms", icon: "forms", activePrefixes: ["/app/forms"] },
      {
        href: "/app/automations",
        label: "Automations",
        icon: "automations",
        activePrefixes: ["/app/automations"],
      },
      { href: "/app/ai", label: "AI Assist", icon: "ai", activePrefixes: ["/app/ai"] },
    ],
  },
  manage: {
    label: "Manage",
    items: [
      { href: "/app/members", label: "Members", icon: "members" },
      { href: "/app/timesheets", label: "Timesheets", icon: "timesheets", activePrefixes: ["/app/timesheets"] },
      {
        href: "/app/reports/time",
        label: "Time reports",
        icon: "reports",
        activePrefixes: ["/app/reports/time"],
      },
      {
        href: "/app/reports/budgets",
        label: "Budgets",
        icon: "reports",
        activePrefixes: ["/app/reports/budgets"],
      },
      { href: "/app/settings", label: "Settings", icon: "settings", activePrefixes: ["/app/settings"] },
    ],
  },
} satisfies Record<string, NavSection>;

export type SettingsGroup = {
  title: string;
  description: string;
  links: Array<{ href: string; label: string; description: string }>;
};

/** Drives /app/settings and keeps every settings page reachable from the command palette. */
export const settingsGroups: SettingsGroup[] = [
  {
    title: "Workspace",
    description: "How the workspace plans, connects and syncs.",
    links: [
      {
        href: "/app/settings/planning",
        label: "Planning",
        description: "Capacity defaults and auto-scheduling rules.",
      },
      {
        href: "/app/settings/task-types",
        label: "Task types",
        description: "Configure workspace task types (Bug, Milestone, ...).",
      },
      {
        href: "/app/settings/time-policy",
        label: "Time policy",
        description: "Timer rounding, overtime and approval requirements.",
      },
      {
        href: "/app/settings/integrations",
        label: "Integrations",
        description: "Calendar, chat and repository connections.",
      },
    ],
  },
  {
    title: "Work",
    description: "Assistive features and where notifications land.",
    links: [
      {
        href: "/app/settings/ai",
        label: "AI provider",
        description: "Model provider, keys and usage limits.",
      },
      {
        href: "/app/notifications/preferences",
        label: "Notification preferences",
        description: "Which events reach you, and on which channel.",
      },
      {
        href: "/app/settings/devices",
        label: "Devices",
        description: "Mobile and browser clients registered for push.",
      },
    ],
  },
  {
    title: "Security & data",
    description: "Access controls, audit trail and data lifecycle.",
    links: [
      {
        href: "/app/settings/security",
        label: "Security",
        description: "Sessions, access tokens and sign-in policy.",
      },
      {
        href: "/app/settings/audit",
        label: "Audit log",
        description: "Every privileged action, with actor and time.",
      },
      {
        href: "/app/settings/retention",
        label: "Data retention",
        description: "How long deleted and archived records are kept.",
      },
      {
        href: "/app/settings/exports",
        label: "Exports",
        description: "Download workspace data as CSV or JSON.",
      },
    ],
  },
];

/**
 * Flat destination list for the command palette. Sections plus the Spaces browser plus every
 * settings page — the sidebar itself only shows a single "Settings" entry.
 */
export const navigation: Array<{ href: string; label: string; activePrefixes?: string[] }> = [
  ...Object.values(navSections).flatMap((section) =>
    section.items.map(({ href, label, activePrefixes }) => ({ href, label, activePrefixes })),
  ),
  { href: "/app/spaces", label: "Spaces", activePrefixes: ["/app/spaces", "/app/lists"] },
  ...settingsGroups.flatMap((group) =>
    group.links.map(({ href, label }) => ({ href, label, activePrefixes: [href] })),
  ),
];

export function isNavItemActive(pathname: string, item: { href: string; activePrefixes?: string[] }) {
  return (
    pathname === item.href || (item.activePrefixes?.some((prefix) => pathname.startsWith(prefix)) ?? false)
  );
}
