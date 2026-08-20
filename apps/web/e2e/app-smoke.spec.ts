import { authStatePath, selectDemoWorkspace } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";
import { DEMO_LIST_URL, SEEDED_TASK_TITLE } from "./helpers/fixtures";
// Imported from the app itself, relative rather than via the "@/" alias (no other e2e file uses the
// alias, so its resolution in Playwright's loader is unproven; nav-config's only import is a type and
// erases). Deriving the list from the nav means a page added to the app is swept automatically —
// a hand-maintained copy would silently stop covering new pages, which is the whole point of this file.
import { navigation } from "../src/components/app-shell/nav-config";

test.use({ storageState: authStatePath("owner") });

/** Routes the nav cannot know about, and the space statuses page added by this branch. */
const EXTRA_ROUTES = [
  { href: DEMO_LIST_URL, label: "List view" },
  { href: `${DEMO_LIST_URL}?view=board`, label: "Board view" },
  { href: "/app/spaces/018f0000-0000-7000-8000-000000011001/statuses", label: "Space statuses" },
];

const ROUTES = [...navigation.map(({ href, label }) => ({ href, label })), ...EXTRA_ROUTES];

/**
 * Every page in the app, loaded and checked for the three failures that make a page useless:
 * it crashed the error boundary, it never rendered real content, or it logged a console error
 * (the console guard fixture asserts that last one automatically for every test in this file).
 *
 * This exists because three shipped pages were reported broken with an error-boundary crash that no
 * existing spec would have caught — every functional spec only visits the handful of pages it needs.
 */
test.describe("every page loads", () => {
  // One shared sign-in and workspace pin for the sweep: re-pinning per route would triple its runtime,
  // and the isolation spec can leave the shell on a workspace this user cannot read.
  test.beforeAll(async ({ browser }) => {
    const page = await browser.newPage({ storageState: authStatePath("owner") });
    await page.goto("/app/my-work");
    await selectDemoWorkspace(page);
    await page.close();
  });

  for (const route of ROUTES) {
    test(`${route.label} (${route.href})`, async ({ page }) => {
      // Heavier pages (gantt, map, dashboards) render a lot before settling.
      await page.goto(route.href, { waitUntil: "domcontentloaded", timeout: 30_000 });

      // app/error.tsx. Checked first and by its own text so the failure message names the real
      // problem instead of a confusing "heading not found".
      await expect(
        page.getByText("Planvexa hit an error"),
        `${route.href} crashed the error boundary`,
      ).toHaveCount(0);

      // Proves real content rather than a permanent skeleton or a blank shell.
      await expect(
        page.getByRole("heading", { level: 1 }).first(),
        `${route.href} rendered no level-1 heading`,
      ).toBeVisible({ timeout: 20_000 });
    });
  }
});

test("an open task detail panel renders", async ({ page }) => {
  await page.goto(DEMO_LIST_URL);
  await page.getByRole("button", { name: SEEDED_TASK_TITLE, exact: true }).click();

  // The panel is a dialog and carries its title in an input, not a heading — so the sweep's generic
  // "has an h1" check cannot speak for it (the list page's own h1 is behind the overlay).
  const panel = page.getByRole("dialog");
  await expect(panel).toBeVisible();
  await expect(page.getByText("Planvexa hit an error")).toHaveCount(0);
  await expect(panel.locator("#task-title")).toHaveValue(SEEDED_TASK_TITLE);
});
