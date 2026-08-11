/**
 * The sidebar icon set — one 24-grid stroke path each, no icon dependency.
 * Multi-stroke glyphs pack their subpaths into the single `d` string.
 */
const iconPaths = {
  activity: "M3 12h4l2-8 4 16 2-8h6",
  ai: "M12 3.5l1.7 4.3 4.3 1.7-4.3 1.7L12 15.5l-1.7-4.3L6 9.5l4.3-1.7zM18 15.5l.8 2 2 .8-2 .8-.8 2-.8-2-2-.8 2-.8z",
  automations: "M13 3L5 14h6l-1 7 8-11h-6z",
  bell: "M6 9a6 6 0 1112 0c0 4 2 5.5 2 5.5H4S6 13 6 9M10 20h4",
  calendar: "M4 6h16v14H4zM4 10h16M8 3.5v4M16 3.5v4",
  chat: "M4 5h16v10H9l-5 4z",
  check: "M4 5h16v15H4zM8 12l2.5 2.5L16 9",
  chevronRight: "M9.5 6l6 6-6 6",
  clips: "M4 6h16v12H4zM9.5 9.5v5l5-2.5z",
  dashboards: "M4 4h7v7H4zM13 4h7v5h-7zM13 13h7v7h-7zM4 15h7v5H4z",
  documents: "M6 3h8l4 4v14H6zM14 3v4h4",
  folder: "M4 6h5l2 2h9v11H4z",
  forms: "M5 4h14v17H5zM9 9h6M9 13h6M9 17h3",
  gantt: "M4 4v16M6.5 7h8M9.5 12h9M6.5 17h5",
  goals: "M12 21a9 9 0 100-18 9 9 0 000 18M12 16a4 4 0 100-8 4 4 0 000 8M12 13a1 1 0 100-2 1 1 0 000 2",
  inbox: "M4 4h16v11l-3 5H7l-3-5zM4 13h5l1.5 2h3l1.5-2h5",
  list: "M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01",
  map: "M4 6l6-2 6 2 6-2v14l-6 2-6-2-6 2zM10 4v14M16 6v14",
  members:
    "M3 20v-1a5 5 0 015-5h1a5 5 0 015 5v1M12 7a4 4 0 11-8 0 4 4 0 018 0M16 14a4 4 0 013 4v2M15.5 4.2a3.5 3.5 0 010 5.6",
  plus: "M12 5.5v13M5.5 12h13",
  reports: "M4 4v16h16M8 16v-4M12 16V8M16 16v-6",
  settings:
    "M12 15a3 3 0 100-6 3 3 0 000 6M12 3.5v2.2M12 18.3v2.2M3.5 12h2.2M18.3 12h2.2M6 6l1.6 1.6M16.4 16.4L18 18M18 6l-1.6 1.6M7.6 16.4L6 18",
  space: "M12 3l8 4.5-8 4.5-8-4.5zM4 12l8 4.5 8-4.5M4 16.5l8 4.5 8-4.5",
  sprints: "M4 9h12l-2.5-2.5M20 15H8l2.5 2.5",
  team: "M8 12a3 3 0 100-6 3 3 0 000 6M17 12a3 3 0 100-6 3 3 0 000 6M2 20a5.5 5.5 0 0111 0M11.5 20a5.5 5.5 0 0111 0",
  timeline: "M4 6h16M4 12h9M4 18h13M15 6l3 3-3 3",
  timesheets: "M12 21a9 9 0 100-18 9 9 0 000 18M12 7.5V12l3 2",
  whiteboard: "M3 5h18v12H3zM8 21h8M12 17v4M7 9l3 3 2-2 4 4",
  workload: "M4 20h16M7.5 20v-5M12 20V7M16.5 20v-8",
} as const;

export type IconName = keyof typeof iconPaths;

export function Icon({ name, className }: { name: IconName; className?: string }) {
  return (
    <svg
      aria-hidden="true"
      focusable="false"
      viewBox="0 0 24 24"
      width="16"
      height="16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
    >
      <path d={iconPaths[name]} />
    </svg>
  );
}
