import type { Locator, Page } from "@playwright/test";
import { authStatePath } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";
import { addTaskInStatus, SANDBOX_LIST_NAME, SANDBOX_LIST_URL } from "./helpers/fixtures";

const boardUrl = `${SANDBOX_LIST_URL}?view=board`;

test.use({ storageState: authStatePath("owner") });

function column(page: Page, status: string) {
  return page.getByRole("region", { name: status, exact: true });
}

async function centerOf(locator: Locator) {
  const box = await locator.boundingBox();

  if (!box) {
    throw new Error("Element has no bounding box; it is not visible.");
  }

  return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
}

/**
 * Creates a card in `status` and narrows the board to it with the page's own title search, so the
 * board stays one column-height tall no matter how many tasks earlier runs left behind.
 */
async function seedCard(page: Page, status: string) {
  const title = `E2E board ${Date.now()}`;

  await addTaskInStatus(page, status, title);
  await expect(column(page, status).getByRole("button", { name: title, exact: true })).toBeVisible();

  await page.getByRole("searchbox", { name: "Search" }).fill(title);
  await expect(page.getByRole("button", { name: /^E2E board /, exact: false })).toHaveCount(1);

  return title;
}

/** Re-applies the title search after a reload, which resets the filter. */
async function searchFor(page: Page, title: string) {
  await page.getByRole("searchbox", { name: "Search" }).fill(title);
}

test("a card dragged with the pointer lands in the target column and survives a reload", async ({
  page,
}) => {
  await page.goto(boardUrl);
  await expect(page.getByRole("heading", { name: SANDBOX_LIST_NAME })).toBeVisible();

  const title = await seedCard(page, "To Do");
  const handle = column(page, "To Do").getByRole("button", { name: `Drag ${title}` });
  const target = column(page, "In Progress");

  const from = await centerOf(handle);
  const to = { x: (await centerOf(target)).x, y: from.y };

  await page.mouse.move(from.x, from.y);
  await page.mouse.down();

  // PointerSensor activates at 8px, so the drag needs real incremental movement — a single
  // mouse.move to the destination is swallowed before the sensor ever starts.
  for (let step = 1; step <= 12; step += 1) {
    await page.mouse.move(
      from.x + ((to.x - from.x) * step) / 12,
      from.y + ((to.y - from.y) * step) / 12,
      { steps: 2 },
    );
  }

  await page.mouse.up();

  await expect(target.getByRole("button", { name: title, exact: true })).toBeVisible();
  await expect(column(page, "To Do").getByRole("button", { name: title, exact: true })).toHaveCount(
    0,
  );

  await page.reload();
  await searchFor(page, title);

  await expect(
    column(page, "In Progress").getByRole("button", { name: title, exact: true }),
  ).toBeVisible();
});

/**
 * The board's keyboard story is the per-card status <select>, not dnd-kit's KeyboardSensor.
 * The sensor does lift and drop (Space toggles `isDragging` and the live region announces the
 * drop), but `sortableKeyboardCoordinates` is a single-container coordinate getter: ArrowRight
 * never resolves a droppable in the *next* column, so the card is always dropped back into its
 * own column. Moving between columns from the keyboard therefore goes through the select, which
 * hits the same `moveTask` mutation as a drag.
 */
test("a card can be moved to another column with the keyboard alone", async ({ page }) => {
  await page.goto(boardUrl);
  await expect(page.getByRole("heading", { name: SANDBOX_LIST_NAME })).toBeVisible();

  const title = await seedCard(page, "To Do");

  await column(page, "To Do")
    .locator("article")
    .filter({ hasText: title })
    .getByRole("combobox")
    .selectOption({ label: "Review" });

  await expect(
    column(page, "Review").getByRole("button", { name: title, exact: true }),
  ).toBeVisible();

  await page.reload();
  await searchFor(page, title);
  await expect(
    column(page, "Review").getByRole("button", { name: title, exact: true }),
  ).toBeVisible();
});
