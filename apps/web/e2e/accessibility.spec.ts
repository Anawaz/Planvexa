import AxeBuilder from "@axe-core/playwright";
import type { Page } from "@playwright/test";
import { authStatePath } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";
import { DEMO_LIST_URL as listUrl, SEEDED_TASK_TITLE as seededTask } from "./helpers/fixtures";

/**
 * Axe rules accepted as-is, with the reason. Prefer fixing the app: this array is for findings
 * that are not the app's to fix. Anything critical or serious that is not listed fails the suite.
 */
const allowlist: string[] = [
  // (empty — every critical/serious finding raised so far was fixed in the components)
];

async function criticalViolations(page: Page) {
  // Entry animations fade elements in, and axe samples whatever opacity it finds mid-flight —
  // which produced a colour-contrast "violation" with a different ratio on every run.
  await page.evaluate(() =>
    Promise.all(document.getAnimations().map((animation) => animation.finished.catch(() => {}))),
  );

  const { violations } = await new AxeBuilder({ page }).analyze();

  return violations
    .filter((violation) => violation.impact === "critical" || violation.impact === "serious")
    .filter((violation) => !allowlist.includes(violation.id))
    .map((violation) => `${violation.id} (${violation.impact}): ${violation.nodes.length} node(s)`);
}

test.describe("signed out", () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test("/login has no critical or serious axe violations", async ({ page }) => {
    await page.goto("/login");
    await expect(page.getByRole("heading", { name: "Log in to Planvexa" })).toBeVisible();

    expect(await criticalViolations(page)).toEqual([]);
  });
});

test.describe("signed in", () => {
  test.use({ storageState: authStatePath("owner") });

  test("/app/my-work has no critical or serious axe violations", async ({ page }) => {
    await page.goto("/app/my-work");
    await expect(page.getByRole("heading", { name: "My Work" })).toBeVisible();

    expect(await criticalViolations(page)).toEqual([]);
  });

  test("the list view has no critical or serious axe violations", async ({ page }) => {
    await page.goto(`${listUrl}?view=list`);
    await expect(page.getByRole("heading", { name: "Current Sprint" })).toBeVisible();

    expect(await criticalViolations(page)).toEqual([]);
  });

  test("the board view has no critical or serious axe violations", async ({ page }) => {
    await page.goto(`${listUrl}?view=board`);
    await expect(page.getByRole("heading", { name: "Current Sprint" })).toBeVisible();

    expect(await criticalViolations(page)).toEqual([]);
  });

  test("the open task detail panel has no critical or serious axe violations", async ({ page }) => {
    await page.goto(`${listUrl}?view=list`);
    // `exact`: the row also carries "Collapse subtasks of …" and "Add subtask to …" buttons.
    await page.getByRole("button", { name: seededTask, exact: true }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await expect(page.getByRole("dialog").locator("#task-title")).toHaveValue(seededTask);

    expect(await criticalViolations(page)).toEqual([]);
  });

  test("/app/timesheets has no critical or serious axe violations", async ({ page }) => {
    await page.goto("/app/timesheets");
    await expect(page.getByRole("heading", { name: "Timesheets" })).toBeVisible();
    // The heading renders before the timesheet loads; scanning earlier catches "Submit timesheet"
    // mid disabled→enabled opacity transition, which axe reads as a contrast failure.
    await expect(page.getByRole("button", { name: "Submit timesheet" })).toBeEnabled();

    expect(await criticalViolations(page)).toEqual([]);
  });
});
