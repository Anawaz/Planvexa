import { authStatePath, loginAs, selectDemoWorkspace } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";
import { addTaskInStatus, SANDBOX_LIST_URL } from "./helpers/fixtures";

// SignalR delivery plus a react-query invalidation round trip; generous but still bounded.
const realtimeTimeout = 15_000;

test.use({ storageState: authStatePath("owner") });

// Two shells, a full Keycloak round trip for the member, and a realtime wait do not fit in the
// 30s default.
test.setTimeout(90_000);

test("a rename by the owner reaches a member's open list without a reload", async ({
  page: ownerPage,
  browser,
}) => {
  const title = `E2E realtime ${Date.now()}`;
  const renamed = `${title} (renamed)`;

  await ownerPage.goto(SANDBOX_LIST_URL);
  await addTaskInStatus(ownerPage, "To Do", title);

  // Explicitly empty: `browser.newContext()` inherits the file-level owner `storageState`, and
  // the inherited Keycloak SSO cookie makes `loginAs` skip the login form and stay the owner.
  const memberContext = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  const memberPage = await memberContext.newPage();

  try {
    // The workspace hub connection is opened by the authenticated shell; wait for the socket so
    // the rename below cannot race the subscription.
    const hubSocket = memberPage.waitForEvent("websocket", { timeout: realtimeTimeout });
    await loginAs(memberPage, "member");
    await selectDemoWorkspace(memberPage);
    await memberPage.goto(SANDBOX_LIST_URL);
    await expect(memberPage.getByRole("button", { name: title, exact: true })).toBeVisible();
    await hubSocket;

    await ownerPage.getByRole("button", { name: title, exact: true }).click();
    const panel = ownerPage.getByRole("dialog");
    await panel.locator("#task-title").fill(renamed);
    await panel.locator("#task-title").blur();
    await panel.getByRole("button", { name: "Close" }).click();
    await expect(ownerPage.getByRole("button", { name: renamed, exact: true })).toBeVisible();

    // No reload: the Task realtime event invalidates the work query root on the member's page.
    await expect(memberPage.getByRole("button", { name: renamed, exact: true })).toBeVisible({
      timeout: realtimeTimeout,
    });
    await expect(memberPage.getByRole("button", { name: title, exact: true })).toHaveCount(0);
  } finally {
    await memberContext.close();
  }
});
