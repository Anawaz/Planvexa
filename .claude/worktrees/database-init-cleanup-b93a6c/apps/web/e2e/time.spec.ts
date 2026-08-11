import { authStatePath } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";
import { createSandboxTask } from "./helpers/fixtures";

// The timesheet grid groups entries by the UTC day of `startedAtUtc`, so the cell id is UTC too.
const todayUtc = new Date().toISOString().slice(0, 10);

test.use({ storageState: authStatePath("owner") });

test("a task timer runs in the global widget and lands on today's timesheet", async ({ page }) => {
  // Its own task, so the seeded demo task does not accumulate a time entry per run.
  const taskTitle = `E2E timer target ${Date.now()}`;
  await createSandboxTask(page, taskTitle);

  const widget = page.getByRole("region", { name: "Active time tracker" });

  // The API allows one running timer per user, so clear a leak from an earlier failed run.
  // The topbar renders the idle branch until the active-timer query resolves, hence the wait
  // rather than a bare isVisible().
  const leaked = await widget
    .waitFor({ state: "visible", timeout: 5_000 })
    .then(() => true)
    .catch(() => false);

  if (leaked) {
    await widget.getByRole("button", { name: "Stop" }).click();
    await expect(widget).toBeHidden();
  }

  await page.getByRole("button", { name: taskTitle, exact: true }).click();

  const panel = page.getByRole("dialog");
  await panel.getByRole("button", { name: "Start timer" }).click();

  // Server-authoritative timer: the widget renders the task title and a live elapsed counter.
  await expect(widget).toBeVisible();
  await expect(widget).toContainText(taskTitle);
  await expect(widget).toContainText("Running");
  await expect(panel.getByRole("button", { name: "Stop timer" })).toBeVisible();

  // The detail panel is a modal overlay covering the topbar, so close it before using the widget.
  await panel.getByRole("button", { name: "Close" }).click();
  await widget.getByRole("button", { name: "Stop" }).click();
  await expect(widget).toBeHidden();
  await expect(page.getByText(/^Stopped at /)).toBeVisible();

  await page.goto("/app/timesheets");
  await expect(page.getByRole("heading", { name: "Timesheets" })).toBeVisible();

  const today = page.locator(`section[aria-labelledby="day-${todayUtc}"]`);
  await expect(today).toBeVisible();
  await expect(today.locator("article").first()).toBeVisible();
  await expect(today.getByText("No time logged.")).toHaveCount(0);
});
