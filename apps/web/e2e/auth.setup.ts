import fs from "node:fs";
import path from "node:path";
import { test as setup } from "@playwright/test";
import { authStatePath, loginAs } from "./helpers/auth";
import { DEMO_WORKSPACE_ID } from "./helpers/fixtures";

// ponytail: owner only — no spec in this chunk runs as another role. `loginAs` takes any dev role,
// so add a second setup test the day a spec actually needs member/admin/guest state.
setup("authenticate as owner", async ({ page }) => {
  await loginAs(page, "owner");

  // Pin the demo workspace into the saved state. Otherwise the shell starts in whichever
  // workspace the API lists first, so an unrelated one — a developer's, or the isolation
  // spec's — silently becomes every spec's context and the seeded lists 404 for no visible reason.
  await page.evaluate(
    ([workspace]) => {
      window.localStorage.setItem("planvexa-active-workspace", workspace);
    },
    [DEMO_WORKSPACE_ID],
  );

  const statePath = authStatePath("owner");
  fs.mkdirSync(path.dirname(statePath), { recursive: true });
  await page.context().storageState({ path: statePath });
});
