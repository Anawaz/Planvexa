import { defineConfig, devices } from "@playwright/test";

// Playwright has no "keep this project out of the default run" flag, so the visual-qa project only
// exists when it was explicitly asked for. The env var carries that decision into the worker
// processes, which re-load this file without the CLI arguments.
if (
  process.argv.some(
    (arg, index) =>
      arg === "--project=visual-qa" ||
      (arg === "--project" && process.argv[index + 1] === "visual-qa"),
  )
) {
  process.env.PLANVEXA_VISUAL_QA = "1";
}
const visualQaRequested = process.env.PLANVEXA_VISUAL_QA === "1";

export default defineConfig({
  testDir: "e2e",
  globalSetup: "./e2e/global-setup.ts",
  // Empties the E2E sandbox list; never fails the run.
  globalTeardown: "./e2e/global-teardown.ts",
  // Local runs get one retry; CI keeps the same budget until flakiness is measured.
  retries: 1,
  // Every write spec drives the same sandbox list, so parallel files fight over the same tasks and
  // invalidate each other's queries. The whole suite runs in well under a minute serially.
  workers: 1,
  reporter: [["list"], ["html", { outputFolder: "e2e/report", open: "never" }]],
  use: {
    baseURL: process.env.PLANVEXA_E2E_BASE_URL ?? "http://localhost:3000",
    trace: "on-first-retry",
  },
  projects: [
    // Logs in once and writes e2e/.auth/*.json so the specs skip the Keycloak round trip.
    { name: "setup", testMatch: /auth\.setup\.ts/ },
    {
      name: "chromium",
      testIgnore: /visual-qa\.spec\.ts/,
      use: { ...devices["Desktop Chrome"] },
      dependencies: ["setup"],
    },
    // Screenshot sweep, not an assertion suite — opt in with `npx playwright test --project=visual-qa`.
    ...(visualQaRequested
      ? [
          {
            name: "visual-qa",
            testMatch: /visual-qa\.spec\.ts/,
            use: { ...devices["Desktop Chrome"] },
            dependencies: ["setup"],
            // A failed screenshot is a page that never settled; retrying just doubles the wait.
            retries: 0,
          },
        ]
      : []),
  ],
});
