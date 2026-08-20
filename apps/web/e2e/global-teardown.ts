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
      extraHTTPHeaders: {
        // The proxy turns this into the API's X-Workspace, exactly as the browser client does.
        "X-Workspace": DEMO_WORKSPACE_ID,
        // This is a bare API request context, not a real page navigation, so it has no
        // Sec-Fetch-Site header -- the proxy's CSRF check (lib/security/csrf.ts) then falls back to
        // requiring a same-origin Origin header, which a request.newContext() call doesn't set by
        // default either. Without this, every mutating call (DELETE included) 403s as "cross-site".
        Origin: baseURL,
      },
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

      // Scratch workflows from statuses.spec.ts. Its own afterEach deletes them through the UI, but
      // that hook does not run when a test times out mid-way — and they accumulate in the demo
      // workspace one failed run at a time (39 of them piled up before this existed). Prefix-scoped,
      // so nothing a human created is ever touched.
      const schemes = await context.get("/api/proxy/status-schemes?workspaceLevelOnly=true");
      if (schemes.ok()) {
        const scratch = ((await schemes.json()) as { id: string; name: string }[]).filter((scheme) =>
          scheme.name.startsWith("E2E "),
        );
        let removed = 0;
        for (const scheme of scratch) {
          if ((await context.delete(`/api/proxy/status-schemes/${scheme.id}`)).ok()) removed += 1;
        }
        if (scratch.length > 0) {
          console.log(`[e2e] teardown removed ${removed}/${scratch.length} scratch workflow(s).`);
        }
      }

      // Throwaway workspaces from workspace-lifecycle.spec.ts, same reasoning. The delete endpoint
      // requires the workspace's own slug as confirmation and must be called from inside it.
      const workspaces = await context.get("/api/proxy/workspaces/me");
      if (workspaces.ok()) {
        const strays = ((await workspaces.json()) as { id: string; name: string; slug: string }[]).filter(
          (workspace) => workspace.name.startsWith("E2E Throwaway"),
        );
        let removed = 0;
        for (const workspace of strays) {
          const result = await context.post(`/api/proxy/workspaces/${workspace.id}/delete`, {
            headers: { "X-Workspace": workspace.id },
            data: { confirmSlug: workspace.slug },
          });
          if (result.ok()) removed += 1;
        }
        if (strays.length > 0) {
          console.log(`[e2e] teardown removed ${removed}/${strays.length} throwaway workspace(s).`);
        }
      }
    } finally {
      await context.dispose();
    }
  } catch (error) {
    console.log(`[e2e] teardown failed, leaving the sandbox as-is: ${String(error)}`);
  }
}
