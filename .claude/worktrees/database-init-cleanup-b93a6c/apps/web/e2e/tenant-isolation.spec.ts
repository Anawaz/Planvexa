import { loginAs } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";
import { DEMO_LIST_NAME, DEMO_LIST_URL, SEEDED_TASK_TITLE } from "./helpers/fixtures";

// A fixed name — and therefore a fixed slug, since the onboarding form slugifies it — so a hundred
// runs leave one extra workspace in the switcher instead of a hundred. Lower-case and hyphenated
// already, so `name === slug`, which is what the switcher's option value holds.
const workspaceName = "e2e-isolation";

// A Keycloak round trip, a workspace registration and two full shell boots do not fit in the default 30s.
test.setTimeout(90_000);

// Opening the demo-workspace list from inside the second workspace is the point of this spec: the API answers
// 404 for every cross-workspace read, and Chrome logs each one as a console error.
test.use({
  consoleAllowlist: [/^Failed to load resource: the server responded with a status of 404 \(Not Found\)$/],
});

test("a second workspace sees none of the demo workspace's data, and switching back restores it", async ({
  page,
}) => {
  await loginAs(page, "member");

  const workspaceSwitcher = page.getByLabel("Current workspace");
  // The demo workspace is always in this member's flat workspace list.
  await expect(workspaceSwitcher).toContainText("Product Operations");
  const alreadyRegistered =
    (await workspaceSwitcher.locator("option", { hasText: workspaceName }).count()) > 0;

  if (alreadyRegistered) {
    // Registered by an earlier run — same workspace, same assertions below.
    await workspaceSwitcher.selectOption({ label: workspaceName });
  } else {
    await page.goto("/onboarding");
    // Workspace-first onboarding (ADR 0015): one field, and its slug === the name here.
    await page.getByLabel("Workspace name").fill(workspaceName);
    await page.getByRole("button", { name: "Create workspace" }).click();
    await page.waitForURL("**/app**");
  }

  await expect(page.getByLabel("Current workspace")).toContainText(workspaceName);

  // Nothing from planvexa-demo may leak into the second workspace.
  await page.goto("/app/spaces");
  // `level: 1` — the sidebar carries its own "Spaces" section heading.
  await expect(page.getByRole("heading", { name: "Spaces", level: 1 })).toBeVisible();
  await expect(page.getByText("Product & Engineering")).toHaveCount(0);
  await expect(page.getByText(DEMO_LIST_NAME)).toHaveCount(0);

  await page.goto("/app/my-work");
  await expect(page.getByText(SEEDED_TASK_TITLE)).toHaveCount(0);

  // A direct link to a demo-workspace list resolves to the error state, not to demo data.
  await page.goto(DEMO_LIST_URL);
  await expect(page.getByRole("heading", { name: "List unavailable" })).toBeVisible();
  await expect(page.getByRole("heading", { name: DEMO_LIST_NAME })).toHaveCount(0);

  // Back to the demo workspace, where this member does have access.
  await page.getByLabel("Current workspace").selectOption({ label: "Product Operations" });
  await expect(page.getByLabel("Current workspace")).toContainText("Product Operations");

  await page.goto(DEMO_LIST_URL);
  await expect(page.getByRole("heading", { name: DEMO_LIST_NAME })).toBeVisible();
  await expect(page.getByRole("button", { name: SEEDED_TASK_TITLE, exact: true })).toBeVisible();
});
