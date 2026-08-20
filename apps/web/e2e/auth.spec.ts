import { loginAs } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";

test("unauthenticated /app redirects to /login and keeps returnTo", async ({ page }) => {
  await page.goto("/app/spaces");

  await expect(page).toHaveURL("/login?returnTo=%2Fapp%2Fspaces");
  await expect(page.getByRole("heading", { name: "Log in to Planvexa" })).toBeVisible();
});

test("owner can log in, is shown in the topbar, and sign-out relocks /app", async ({ page }) => {
  await loginAs(page, "owner");

  await expect(page).toHaveURL(/\/app\//);
  const account = page.locator("header summary");
  await expect(account).toContainText("Dev Owner");

  await account.click();
  await page.getByRole("menuitem", { name: "Sign out" }).click();
  // /auth/logout hands off to Keycloak's end-session endpoint with post_logout_redirect_uri = "/",
  // so sign-out lands on the public home page, not /login. Either is fine here — the point of this
  // wait is only that we have left /app; the assertion below is what proves the session is dead.
  await page.waitForURL((url) => url.pathname === "/" || url.pathname.startsWith("/login"));

  await page.goto("/app");
  await expect(page).toHaveURL(/\/login\?returnTo=/);
});
