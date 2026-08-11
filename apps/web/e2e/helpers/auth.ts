import path from "node:path";
import { expect, type Page } from "@playwright/test";
import { DEMO_WORKSPACE_NAME } from "./fixtures";

export type DevRole = "owner" | "admin" | "member" | "guest";

export const devPassword = process.env.PLANVEXA_DEV_PASSWORD ?? "PlanvexaDev!123";

export function authStatePath(role: DevRole) {
  return path.join(__dirname, "..", ".auth", `${role}.json`);
}

/** Full login round trip: /login → Keycloak form → back inside /app. */
export async function loginAs(page: Page, role: DevRole) {
  await page.goto("/login");
  await page.getByRole("link", { name: "Continue with Keycloak" }).click();
  await page.locator("#username").fill(`${role}@planvexa.local`);
  await page.locator("#password").fill(devPassword);
  await page.locator("#kc-login").click();
  await page.waitForURL("**/app/**");
}

/**
 * Pins the shell to the seeded workspace. The shell otherwise defaults to the alphabetically first
 * workspace, and the isolation spec leaves the member owning `e2e-isolation`, which sorts
 * ahead of the demo one.
 */
export async function selectDemoWorkspace(page: Page) {
  await page.getByLabel("Current workspace").selectOption({ label: DEMO_WORKSPACE_NAME });
  await expect(page.getByLabel("Current workspace")).toContainText(DEMO_WORKSPACE_NAME);
}
