import { expect, type Page } from "@playwright/test";

/**
 * Seeded identifiers, in one place.
 *
 * Write specs must target the sandbox list: it is seeded empty (PlanvexaDevelopmentSeeder.cs) and
 * emptied again by global-teardown, so a few hundred runs cannot turn the demo workspace into a
 * pile of "E2E … (renamed)" rows. Read-only specs (visual-qa, accessibility) stay on the demo list
 * on purpose — screenshots and axe scans need realistic data.
 */
export const DEMO_LIST_ID = "018f0000-0000-7000-8000-000000012001";
export const DEMO_LIST_URL = `/app/lists/${DEMO_LIST_ID}`;
export const DEMO_LIST_NAME = "Current Sprint";
export const SEEDED_TASK_TITLE = "Wire real API clients";

export const SANDBOX_LIST_ID = "018f0000-0000-7000-8000-000000012901";
export const SANDBOX_LIST_URL = `/app/lists/${SANDBOX_LIST_ID}`;
export const SANDBOX_LIST_NAME = "E2E Sandbox";

export const DEMO_WORKSPACE_NAME = "Product Operations";
export const DEMO_WORKSPACE_ID = "018f0000-0000-7000-8000-000000000101";

/**
 * Creates a task through a status group's (or board column's) inline composer.
 *
 * The composer is no longer always on screen: each group ends with an "Add task in {Status}"
 * button that swaps itself for the composer. Every spec goes through here so the reveal-then-type
 * dance lives in one place.
 */
export async function addTaskInStatus(page: Page, status: string, title: string) {
  const composer = page.getByRole("textbox", { name: `New task in ${status}`, exact: true });

  // Already revealed by an earlier call on this page? Then skip straight to typing.
  if (!(await composer.isVisible())) {
    await page.getByRole("button", { name: `Add task in ${status}`, exact: true }).click();
  }

  await composer.fill(title);
  await composer.press("Enter");
  await expect(page.getByRole("button", { name: title, exact: true })).toBeVisible();
}

/** Opens the sandbox list and creates a task through the "To Do" composer. */
export async function createSandboxTask(page: Page, title: string) {
  await page.goto(SANDBOX_LIST_URL);
  await expect(page.getByRole("heading", { name: SANDBOX_LIST_NAME })).toBeVisible();

  await addTaskInStatus(page, "To Do", title);
}
