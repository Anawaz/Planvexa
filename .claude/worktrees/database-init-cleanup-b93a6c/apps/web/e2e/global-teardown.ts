import fs from "node:fs";
import { request } from "@playwright/test";
import { authStatePath } from "./helpers/auth";
import { DEMO_WORKSPACE_ID, SANDBOX_LIST_ID } from "./helpers/fixtures";

/**
 * Drains the E2E sandbox list after the run, so a few hundred runs do not turn it into a wall of
 * "E2E … (renamed)" rows. The sandbox is seeded empty, so everything in it was created by a spec
 * and everything in it goes.
 *
 * Deletes are issued straight against the API through the BFF proxy — the browser-side delete
 * button is another agent's business and this must work with or without it.
 *
 * Never fails the run: a teardown that reds a green suite is worse than a full sandbox.
 */
export default async function globalTeardown() {
  const baseURL = process.env.PLANVEXA_E2E_BASE_URL ?? "http://localhost:3000";
  const storageState = authStatePath("owner");

  try {
    if (!fs.existsSync(storageState)) {
      console.log("[e2e] teardown skipped: no owner auth state (the setup project never ran).");
      return;
    }

    const context = await request.newContext({
      baseURL,
      storageState,
      // The proxy turns these into the API's X-Workspace, exactly as the browser client does.
      extraHTTPHeaders: { "X-Workspace": DEMO_WORKSPACE_ID },
    });

    try {
      const response = await context.get(`/api/proxy/lists/${SANDBOX_LIST_ID}/tasks`);
      if (!response.ok()) {
        console.log(`[e2e] teardown skipped: sandbox list read returned ${response.status()}.`);
        return;
      }

      const tasks = (await response.json()) as { id: string }[];
      let deleted = 0;
      for (const task of tasks) {
        const result = await context.delete(`/api/proxy/tasks/${task.id}`);
        if (result.ok()) {
          deleted += 1;
        } else {
          console.log(`[e2e] teardown could not delete ${task.id}: ${result.status()}.`);
        }
      }

      console.log(`[e2e] teardown removed ${deleted}/${tasks.length} sandbox task(s).`);
    } finally {
      await context.dispose();
    }
  } catch (error) {
    console.log(`[e2e] teardown failed, leaving the sandbox as-is: ${String(error)}`);
  }
}
