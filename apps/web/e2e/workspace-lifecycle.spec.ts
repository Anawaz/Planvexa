import { authStatePath, selectDemoWorkspace } from "./helpers/auth";
import { DEMO_WORKSPACE_NAME } from "./helpers/fixtures";
import { expect, test } from "./helpers/console-guard";

test.use({
  storageState: authStatePath("owner"),
  // This spec switches workspaces three times (into the new one, then out of it after the delete).
  // Each switch refetches `workKeys.lists(spaceId)` for spaces cached from the workspace being left;
  // those ids do not exist in the one being entered, so the sidebar briefly 404s — and 403s while
  // the shell is pointed at a workspace another spec left behind. Both are pre-existing and
  // reproducible with the plain workspace switcher: the keys in lib/work/queries.ts are not
  // workspace-scoped. Allowlisted for this spec rather than muted globally.
  consoleAllowlist: ["Failed to load resource: the server responded with a status of 40[34]"],
});

/**
 * Creating and then permanently deleting a throwaway workspace, end to end through the UI.
 *
 * This spec only ever deletes the workspace it just created itself — never the demo one — and the
 * delete is what cleans it up, so a green run leaves the account exactly as it found it.
 */
// Unique per run: slugs are globally unique, so a workspace stranded by an earlier failed run would
// otherwise make every later run fail with a 409 on create.
const RUN_ID = Date.now();
const THROWAWAY_NAME = `E2E Throwaway ${RUN_ID}`;
const THROWAWAY_SLUG = `e2e-throwaway-${RUN_ID}`;

test("a workspace can be created from the switcher and permanently deleted", async ({ page }) => {
  await page.goto("/app/settings/workspace");

  const switcher = page.getByLabel("Current workspace");
  await expect(switcher).toBeVisible();
  // The isolation spec leaves the shell pointed at a workspace this user cannot read, and it sorts
  // ahead of the demo one — start from a known workspace so "the survivor" below is meaningful.
  await selectDemoWorkspace(page);

  // 1. The "+" beside the switcher is the in-app entry point that did not exist before.
  await page.getByRole("link", { name: "Create workspace" }).click();
  await page.waitForURL("**/onboarding");

  await page.getByLabel("Workspace name").fill(THROWAWAY_NAME);
  await page.getByRole("button", { name: "Create workspace" }).click();

  // The shell re-bootstraps INTO the new workspace — creating one should also switch to it.
  // Asserting on the selected option, not the select's text: a <select> "contains" every option,
  // so toContainText would pass merely because the workspace exists in the list.
  await page.waitForURL("**/app**");
  await expect(switcher.locator("option:checked")).toHaveText(THROWAWAY_NAME);

  // 2. Delete it: Owner-only, and gated on retyping the slug exactly.
  await page.goto("/app/settings/workspace");
  await expect(page.getByRole("heading", { name: THROWAWAY_NAME, level: 1 })).toBeVisible();

  const confirm = page.getByLabel(/Type .* to confirm/);
  const deleteButton = page.getByRole("button", { name: "Delete workspace permanently" });

  await expect(deleteButton).toBeDisabled();
  await confirm.fill("not-the-slug");
  await expect(deleteButton).toBeDisabled();
  await confirm.fill(THROWAWAY_SLUG);
  await expect(deleteButton).toBeEnabled();

  await deleteButton.click();

  // On success the page hard-navigates to /app; on failure it stays here and renders an inline
  // alert. Waiting for that navigation specifically — "**/app**" would match the settings page we
  // are already on, so it returned instantly and let the rest of the test race a delete that had
  // not landed yet.
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(page).toHaveURL(/\/app\/?$/, { timeout: 15_000 });

  // 3. Back inside a surviving workspace, with the throwaway gone from the switcher entirely.
  await page.goto("/app/settings/workspace");
  await expect(switcher).toBeVisible();
  await expect(switcher.locator("option", { hasText: THROWAWAY_NAME })).toHaveCount(0);

  // 4. The demo workspace — the one with all the real seeded data — survived, and we are in it.
  await expect(switcher.locator("option", { hasText: DEMO_WORKSPACE_NAME })).toHaveCount(1);
  await expect(page.getByRole("heading", { level: 1 })).not.toHaveText(THROWAWAY_NAME);
});
