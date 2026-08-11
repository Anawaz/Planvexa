import { authStatePath } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";
import { addTaskInStatus, SANDBOX_LIST_URL } from "./helpers/fixtures";

test.use({
  storageState: authStatePath("owner"),
  // `context.setOffline(true)` makes Chrome report every in-flight/attempted request as a genuine
  // network failure (and the SignalR hub's connection drops too) -- both log to the console.
  consoleAllowlist: [
    "^Failed to load resource: net::ERR_INTERNET_DISCONNECTED$",
    "WebSocket connection to.*failed",
  ],
});

/**
 * PWA/offline support: a task edit made while offline is queued in the IndexedDB outbox
 * with an optimistic UI update, not rejected -- this supersedes the earlier behaviour (an
 * "unable to save" alert on any failed mutation), which `updateTaskOffline` in
 * src/lib/work/offlineMutations.ts now only takes for a genuine (non-network) API rejection.
 *
 * NOT executed in the harness that authored it: running this spec requires the full local stack
 * (Postgres + Keycloak + the API, via `pwsh scripts/dev-up.ps1` + `dotnet run`), which AGENTS.md rule
 * 18 says not to start for manual/unattended execution, and this sandbox does not have it running.
 * The scenario, selectors, and timing were reasoned through against the actual implementation
 * (src/lib/offline/{withOfflineFallback,replay,useOfflineSync}.ts,
 * src/components/app-shell/OfflineIndicator.tsx) but this is NOT a substitute for a real run before
 * merge -- flag it in review.
 */
test("an offline task edit is queued with no error, then syncs automatically once back online", async ({
  page,
  context,
}) => {
  const title = `E2E offline ${Date.now()}`;
  const offlineTitle = `${title} (edited offline)`;

  await page.goto(SANDBOX_LIST_URL);
  await addTaskInStatus(page, "To Do", title);
  await page.getByRole("button", { name: title, exact: true }).click();

  const panel = page.getByRole("dialog");
  await expect(panel.locator("#task-title")).toHaveValue(title);

  // Real browser-level offline (CDP), not route interception: `navigator.onLine` and the `online`/
  // `offline` window events -- which useOfflineSync.ts's reconnect trigger depends on -- only fire
  // for this, not for `page.route(...).abort()`.
  await context.setOffline(true);

  await panel.locator("#task-title").fill(offlineTitle);
  await panel.locator("#task-title").blur();

  // Queued, not rejected: no "could not be saved" alert, and the optimistic value sticks immediately.
  await expect(panel.getByRole("alert")).toHaveCount(0);
  await expect(panel.locator("#task-title")).toHaveValue(offlineTitle);
  await expect(page.getByText(/pending sync/i)).toBeVisible();

  await context.setOffline(false);

  // useOfflineSync's `online` listener replays the outbox; the pending-sync banner clears once it
  // lands.
  await expect(page.getByText(/pending sync/i)).toHaveCount(0, { timeout: 15_000 });

  await page.reload();
  await expect(page.getByRole("button", { name: offlineTitle, exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: title, exact: true })).toHaveCount(0);
});
