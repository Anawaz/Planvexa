import type { Page } from "@playwright/test";
import { authStatePath } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";

test.use({ storageState: authStatePath("owner") });

/** Seeded spaces in the demo workspace — two of them, which is what makes the isolation check real. */
const SPACE_A = { id: "018f0000-0000-7000-8000-000000011001", name: "Product & Engineering" };
const SPACE_B = { id: "018f0000-0000-7000-8000-000000011002", name: "Go-to-Market" };

const STATUSES_URL = "/app/settings/statuses";

function spaceStatusesUrl(spaceId: string) {
  return `/app/spaces/${spaceId}/statuses`;
}

function schemeCard(page: Page, name: string) {
  return page.locator(`section[aria-label="${name}"]`);
}

/**
 * The workspace default scheme's card, found by its "Default" badge — the demo seed calls it
 * "Default delivery workflow" and a spec should not depend on that name.
 */
function defaultSchemeCard(page: Page) {
  return page.locator("section[aria-label]").filter({ has: page.getByText("Default", { exact: true }) });
}

/**
 * Creates a throwaway workflow to edit, so no test ever mutates the seeded default that every other
 * spec (and the demo data) depends on. The unique name also means a leftover from a previously
 * failed run can never turn into a strict-mode violation here.
 */
async function createScratchWorkflow(page: Page, template = "Kanban") {
  const name = `E2E ${template} ${Date.now()}`;
  await page.goto(STATUSES_URL);

  const createForm = page.locator('section[aria-labelledby="new-workflow-title"]');
  await createForm.getByLabel("Name").fill(name);
  await createForm.getByLabel("Template").selectOption(template);
  await createForm.getByRole("button", { name: "Create workflow" }).click();
  await expect(schemeCard(page, name)).toBeVisible();

  return name;
}

/** Deletes every workflow this suite created, including any stranded by an earlier failed run. */
async function deleteScratchWorkflows(page: Page) {
  await page.goto(STATUSES_URL);
  const scratch = page.locator('section[aria-label^="E2E "]');

  for (let remaining = await scratch.count(); remaining > 0; remaining = await scratch.count()) {
    page.once("dialog", (d) => void d.accept());
    await scratch.first().getByRole("button", { name: "Delete workflow" }).click();
    await expect(scratch).toHaveCount(remaining - 1);
  }
}

/** Leaves the Space back on the workspace default, whatever state the test got it into. */
async function revertToWorkspaceDefault(page: Page, spaceId: string) {
  await page.goto(spaceStatusesUrl(spaceId));

  const inherited = page.getByText("This space uses the workspace default.");
  const revert = page.getByRole("button", { name: "Use workspace default" });
  // Wait for one of the two banners so this never races the initial load.
  await expect(inherited.or(revert)).toBeVisible();
  if (await inherited.isVisible()) {
    return;
  }

  await revert.click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();

  const submit = dialog.getByRole("button", { name: "Use workspace default" });
  // Enabled only once the workspace-default targets have loaded — wait for that rather than reading
  // options that do not exist yet.
  await expect(submit).toBeEnabled();
  // Every row prefills to a real target by name; anything unmatched gets the first available one.
  for (const select of await dialog.locator("select").all()) {
    if ((await select.inputValue()) === "") {
      await select.selectOption({ index: 1 });
    }
  }

  await submit.click();
  await expect(inherited).toBeVisible();
}

test.describe("workspace default statuses", () => {
  test.afterEach(async ({ page }) => {
    await deleteScratchWorkflows(page);
  });

  test("add, rename, recategorize and reorder a status", async ({ page }) => {
    const workflow = await createScratchWorkflow(page, "Simple");
    const card = schemeCard(page, workflow);

    await card.getByLabel("New status name").fill("E2E Draft");
    await card.getByLabel("New status category").selectOption("Active");
    await card.getByRole("button", { name: "Add status" }).click();
    await expect(card.getByLabel("Name of E2E Draft")).toBeVisible();

    // Rename commits on blur, not per keystroke.
    const nameField = card.getByLabel("Name of E2E Draft");
    await nameField.fill("E2E Renamed");
    await nameField.blur();
    await expect(card.getByLabel("Name of E2E Renamed")).toBeVisible();

    await card.getByLabel("Category of E2E Renamed").selectOption("Done");
    await expect(card.getByLabel("Category of E2E Renamed")).toHaveValue("Done");

    // Appended last, so it can move up but not down — and that flips once it has moved.
    await expect(card.getByLabel("Move E2E Renamed down")).toBeDisabled();
    await card.getByLabel("Move E2E Renamed up").click();
    await expect(card.getByLabel("Move E2E Renamed down")).toBeEnabled();
  });

  test("removing a status cannot proceed without choosing where its tasks go", async ({ page }) => {
    const workflow = await createScratchWorkflow(page, "Kanban");
    const card = schemeCard(page, workflow);

    await card.getByLabel("Remove Blocked").click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole("heading", { name: /Remove/ })).toBeVisible();

    const confirm = dialog.getByRole("button", { name: "Remove and move tasks" });
    // Prefilled with a sensible target, so the common case is one click...
    await expect(confirm).toBeEnabled();
    // ...but clearing the choice hard-disables it. This is the guarantee that matters: a status is
    // never removed without naming somewhere for its tasks to land.
    await dialog.getByLabel("Replacement status").selectOption("");
    await expect(confirm).toBeDisabled();

    // Choosing a target lets it through, and the status is gone.
    await dialog.getByLabel("Replacement status").selectOption({ label: "To Do" });
    await expect(confirm).toBeEnabled();
    await confirm.click();
    await expect(card.getByLabel("Name of Blocked")).toHaveCount(0);
    await expect(card.getByLabel("Name of To Do")).toBeVisible();
  });

  test("a workflow can be created from a template, and the default one cannot be deleted", async ({ page }) => {
    const workflow = await createScratchWorkflow(page, "Kanban");

    // The Kanban preset's statuses.
    for (const status of ["Backlog", "To Do", "In Progress", "Blocked", "Done"]) {
      await expect(schemeCard(page, workflow).getByLabel(`Name of ${status}`)).toBeVisible();
    }

    // The seeded default is protected; the scratch one is not.
    await expect(defaultSchemeCard(page).getByRole("button", { name: "Delete workflow" })).toBeDisabled();
    await expect(schemeCard(page, workflow).getByRole("button", { name: "Delete workflow" })).toBeEnabled();
  });
});

/** Deliberately provokes a failed request, so it gets its own guard and no scratch-cleanup hook. */
test.describe("statuses page failure handling", () => {
  test.use({ consoleAllowlist: ["Failed to load resource: the server responded with a status of 500"] });

  test("a failed load reports an error instead of looking empty", async ({ page }) => {
    // The page used to render `data ?? []`, so an API failure showed "No workflows yet." — which
    // reads as "this workspace has none" and hides every editor, making a transient outage look
    // exactly like a missing feature. It must say the load failed.
    await page.route("**/status-schemes**", (route) => route.fulfill({ status: 500, body: "{}" }));

    await page.goto(STATUSES_URL);

    // QueryState's own error card, by its copy — a bare getByRole("alert") would also be satisfied
    // by the Next.js dev-tools overlay's empty alert region, which proves nothing.
    await expect(page.getByRole("alert").filter({ hasText: "Something went wrong" })).toBeVisible();
    await expect(page.getByText("No workflows yet.")).toHaveCount(0);
    await expect(page.getByText("This workspace has no workflows. Create one above.")).toHaveCount(0);
  });
});

test.describe("per-space status overrides", () => {
  test.afterEach(async ({ page }) => {
    await revertToWorkspaceDefault(page, SPACE_B.id);
  });

  test("a space inherits the workspace default and says so", async ({ page }) => {
    // The exact URL that used to throw "workKeys.spaceStatusScheme is not a function". The console
    // guard fails this test on any page error, so that regression cannot come back silently.
    await page.goto(spaceStatusesUrl(SPACE_A.id));

    await expect(page.getByRole("heading", { name: "Statuses & workflow" })).toBeVisible();
    await expect(page.getByLabel("Statuses & workflow").getByText(SPACE_A.name)).toBeVisible();
    await expect(page.getByText("This space uses the workspace default.")).toBeVisible();

    // Inherited means read-only: editing here would change every other inheriting space.
    await expect(page.getByLabel("Name of To Do")).toBeDisabled();
    await expect(page.getByRole("button", { name: "Customize this space" })).toBeEnabled();
  });

  test("customizing one space leaves the other space and the workspace default untouched", async ({ page }) => {
    await page.goto(spaceStatusesUrl(SPACE_B.id));
    await page.getByRole("button", { name: "Customize this space" }).click();
    await expect(page.getByText("Custom to this space.")).toBeVisible();

    // The clone is lossless, so the inherited statuses are all still here — and now editable.
    const review = page.getByLabel("Name of Review");
    await expect(review).toBeEnabled();
    await review.fill("B-Only Review");
    await review.blur();
    await expect(page.getByLabel("Name of B-Only Review")).toBeVisible();

    // The other space still inherits, and still shows the original name.
    await page.goto(spaceStatusesUrl(SPACE_A.id));
    await expect(page.getByText("This space uses the workspace default.")).toBeVisible();
    await expect(page.getByLabel("Name of Review")).toBeVisible();
    await expect(page.getByLabel("Name of B-Only Review")).toHaveCount(0);

    // And neither did the workspace default itself.
    await page.goto(STATUSES_URL);
    await expect(defaultSchemeCard(page).getByLabel("Name of Review")).toBeVisible();
    await expect(page.getByLabel("Name of B-Only Review")).toHaveCount(0);
  });

  test("reverting a space to the workspace default restores inheritance", async ({ page }) => {
    await page.goto(spaceStatusesUrl(SPACE_B.id));
    await page.getByRole("button", { name: "Customize this space" }).click();
    await expect(page.getByText("Custom to this space.")).toBeVisible();

    await revertToWorkspaceDefault(page, SPACE_B.id);

    await expect(page.getByText("This space uses the workspace default.")).toBeVisible();
    await expect(page.getByLabel("Name of To Do")).toBeDisabled();
  });
});
