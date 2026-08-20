import type { StatusInput } from "./client";

// Colours are the ones StatusScheme.CreateDefault uses, so a preset-built workflow looks like the
// built-in Default: grey for not-started, blue/purple for active, green for done/closed.
const grey = "#8b8b8b";
const blue = "#2b7fff";
const purple = "#a855f7";
const green = "#12b76a";

/** Starting points offered when creating a workflow or customizing a Space — purely client-side;
 * the backend just receives the resulting status list. */
export const statusPresets: { name: string; statuses: StatusInput[] }[] = [
  {
    name: "Kanban",
    statuses: [
      { name: "Backlog", category: "NotStarted", color: grey },
      { name: "To Do", category: "NotStarted", color: grey },
      { name: "In Progress", category: "Active", color: blue },
      { name: "Blocked", category: "Active", color: purple },
      { name: "Done", category: "Done", color: green },
    ],
  },
  {
    name: "Scrum",
    statuses: [
      { name: "Product Backlog", category: "NotStarted", color: grey },
      { name: "Sprint Backlog", category: "NotStarted", color: grey },
      { name: "In Progress", category: "Active", color: blue },
      { name: "In Review", category: "Active", color: purple },
      { name: "Done", category: "Done", color: green },
    ],
  },
  {
    name: "Bug tracking",
    statuses: [
      { name: "Open", category: "NotStarted", color: grey },
      { name: "Triaged", category: "NotStarted", color: grey },
      { name: "In Progress", category: "Active", color: blue },
      { name: "Fixed", category: "Active", color: purple },
      { name: "Verified", category: "Done", color: green },
      { name: "Closed", category: "Closed", color: green },
    ],
  },
  {
    name: "Simple",
    statuses: [
      { name: "To Do", category: "NotStarted", color: grey },
      { name: "Done", category: "Done", color: green },
    ],
  },
];
