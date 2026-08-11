import { authStatePath } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";
import { addTaskInStatus, SANDBOX_LIST_NAME, SANDBOX_LIST_URL } from "./helpers/fixtures";

test.use({ storageState: authStatePath("owner") });

test("a task survives create, rename, status change and completion across a reload", async ({
  page,
}) => {
  const title = `E2E task ${Date.now()}`;
  const renamed = `${title} (renamed)`;

  await page.goto(SANDBOX_LIST_URL);
  await expect(page.getByRole("heading", { name: SANDBOX_LIST_NAME })).toBeVisible();

  // Create through the "To Do" list composer.
  await addTaskInStatus(page, "To Do", title);

  // Rename and re-status it from the detail panel. `exact`: the row also carries an
  // "Add subtask to {title}" button, so a substring match is ambiguous.
  await page.getByRole("button", { name: title, exact: true }).click();
  const panel = page.getByRole("dialog");
  await panel.locator("#task-title").fill(renamed);
  await panel.locator("#task-title").blur();
  await panel.getByLabel("Status").selectOption({ label: "In Progress" });
  await expect(panel.getByLabel("Status")).toHaveValue(
    "018f0000-0000-7000-8000-000000010103",
  );
  await panel.getByRole("button", { name: "Close" }).click();

  const row = page.locator("article").filter({ hasText: renamed });
  await expect(row.getByText("In Progress", { exact: true })).toBeVisible();

  // Complete it from the list checkbox. `click`, not `check`: completing moves the row into the
  // "Complete" group, so React replaces the input and `check`'s post-click assertion reads a
  // detached node. Rows now also carry a selection checkbox, hence the name.
  const completedBox = page
    .locator("article")
    .filter({ hasText: renamed })
    .getByRole("checkbox", { name: `Reopen ${renamed}` });

  await row.getByRole("checkbox", { name: `Complete ${renamed}` }).click();
  await expect(completedBox).toBeChecked();

  await page.reload();

  const reloaded = page.locator("article").filter({ hasText: renamed });
  await expect(completedBox).toBeChecked();
  await expect(reloaded.getByText("Complete", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: title, exact: true })).toHaveCount(0);
});
