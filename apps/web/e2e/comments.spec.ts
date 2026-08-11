import { authStatePath } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";
import { createSandboxTask } from "./helpers/fixtures";

test.use({ storageState: authStatePath("owner") });

// The comment lands on a task this spec creates, not on the seeded demo task. The assertions only
// need *some* task, and the demo one would otherwise collect one comment per run forever.
test("a comment persists across a reload", async ({ page }) => {
  const taskTitle = `E2E comment target ${Date.now()}`;
  const body = `E2E comment ${Date.now()}`;

  await createSandboxTask(page, taskTitle);
  await page.getByRole("button", { name: taskTitle, exact: true }).click();

  const panel = page.getByRole("dialog");
  await panel.getByRole("textbox", { name: "New comment" }).fill(body);
  await panel.getByRole("button", { name: "Comment", exact: true }).click();

  const comment = panel.locator("article").filter({ hasText: body });
  await expect(comment).toBeVisible();
  await expect(comment.getByRole("heading", { name: "Dev Owner" })).toBeVisible();

  await page.reload();

  // Opening a task now writes `?task={id}`, so the reload reopens the drawer by itself — the
  // comment still has to come back from the server, which is what this spec is about.
  await expect(page.getByRole("dialog").locator("#task-title")).toHaveValue(taskTitle);

  const reloaded = page.getByRole("dialog").locator("article").filter({ hasText: body });
  await expect(reloaded).toBeVisible();
  await expect(reloaded.getByRole("heading", { name: "Dev Owner" })).toBeVisible();
});
