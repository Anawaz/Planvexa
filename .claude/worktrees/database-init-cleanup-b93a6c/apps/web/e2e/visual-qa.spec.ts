import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { authStatePath } from "./helpers/auth";
import { DEMO_LIST_URL as listUrl, SEEDED_TASK_TITLE as seededTask } from "./helpers/fixtures";

/**
 * Screenshot sweep for manual review — every page × viewport × colour scheme lands in
 * e2e/visual-qa-output/ (gitignored). It asserts only that each page reached a readable state in
 * the requested theme; a human (or agent) judges the pixels afterwards.
 *
 * Excluded from the default run — see the `visual-qa` project in playwright.config.ts.
 */

const outputDir = path.join(__dirname, "visual-qa-output");
const viewports = {
  desktop: { width: 1440, height: 900 },
  tablet: { width: 768, height: 1024 },
  mobile: { width: 390, height: 844 },
} as const;

const schemes = ["light", "dark"] as const;

type Scheme = (typeof schemes)[number];

/**
 * The app reads `planvexa-theme` from localStorage on first render and falls back to the media
 * query, then toggles `.dark` on <html>. Seed both so the very first paint is already themed —
 * flipping it after load would screenshot a half-transitioned page.
 */
async function useScheme(page: Page, scheme: Scheme) {
  await page.emulateMedia({ colorScheme: scheme });
  await page.addInitScript((value) => {
    window.localStorage.setItem("planvexa-theme", value);
  }, scheme);
}

async function shoot(page: Page, name: string, viewport: string, scheme: Scheme) {
  // Entry animations fade and slide content in; a screenshot mid-flight is not the real layout.
  await page.evaluate(() =>
    Promise.all(document.getAnimations().map((animation) => animation.finished.catch(() => {}))),
  );
  await page.waitForLoadState("networkidle");

  // The whole point of the sweep is dark-mode regressions — a page that silently rendered light
  // would produce 21 useless screenshots.
  expect(await page.evaluate(() => document.documentElement.classList.contains("dark"))).toBe(
    scheme === "dark",
  );

  await page.screenshot({
    path: path.join(outputDir, `${name}-${viewport}-${scheme}.png`),
    fullPage: true,
  });
}

/** Each target: navigate, then wait for the one element that proves the page has real content. */
const targets: Record<string, (page: Page) => Promise<void>> = {
  "my-work": async (page) => {
    await page.goto("/app/my-work");
    await expect(page.getByRole("heading", { name: "My Work" })).toBeVisible();
  },
  "list-list": async (page) => {
    await page.goto(`${listUrl}?view=list`);
    await expect(page.getByRole("heading", { name: "Current Sprint" })).toBeVisible();
    await expect(page.getByRole("button", { name: seededTask, exact: true })).toBeVisible();
  },
  "list-board": async (page) => {
    await page.goto(`${listUrl}?view=board`);
    await expect(page.getByRole("heading", { name: "Current Sprint" })).toBeVisible();
    await expect(page.getByRole("button", { name: seededTask, exact: true })).toBeVisible();
  },
  "task-detail": async (page) => {
    await page.goto(`${listUrl}?view=list`);
    await page.getByRole("button", { name: seededTask, exact: true }).click();
    await expect(page.getByRole("dialog").locator("#task-title")).toHaveValue(seededTask);
  },
  timesheets: async (page) => {
    await page.goto("/app/timesheets");
    await expect(page.getByRole("heading", { name: "Timesheets" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Submit timesheet" })).toBeEnabled();
  },
  chat: async (page) => {
    await page.goto("/app/chat");
    await expect(page.getByRole("heading", { name: "Chat", exact: true })).toBeVisible();
  },
};

test.describe("signed out", () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  for (const [viewport, size] of Object.entries(viewports)) {
    for (const scheme of schemes) {
      test(`login — ${viewport} ${scheme}`, async ({ page }) => {
        await page.setViewportSize(size);
        await useScheme(page, scheme);
        await page.goto("/login");
        await expect(page.getByRole("heading", { name: "Log in to Planvexa" })).toBeVisible();
        await shoot(page, "login", viewport, scheme);
      });
    }
  }
});

test.describe("signed in", () => {
  test.use({ storageState: authStatePath("owner") });

  for (const [name, open] of Object.entries(targets)) {
    for (const [viewport, size] of Object.entries(viewports)) {
      for (const scheme of schemes) {
        test(`${name} — ${viewport} ${scheme}`, async ({ page }) => {
          await page.setViewportSize(size);
          await useScheme(page, scheme);
          await open(page);
          await shoot(page, name, viewport, scheme);
        });
      }
    }
  }
});
