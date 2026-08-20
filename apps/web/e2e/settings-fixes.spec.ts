import { authStatePath } from "./helpers/auth";
import { expect, test } from "./helpers/console-guard";

test.use({ storageState: authStatePath("owner") });

const ORIGINAL_NAME = "Dev Owner";
const RENAMED = "Renamed Owner";

/**
 * The topbar used to read the display name off the Keycloak session cookie, which is only refreshed
 * at sign-in — so renaming yourself left the old name on screen until you logged out and back in.
 * It now prefers the account record the profile page actually writes to.
 */
test("renaming yourself in the profile updates the topbar without a reload", async ({ page }) => {
  await page.goto("/app/settings/profile");
  const displayName = page.getByLabel("Display name");
  await expect(displayName).toHaveValue(ORIGINAL_NAME);

  const account = page.locator("header summary");
  await expect(account).toContainText(ORIGINAL_NAME);

  try {
    await displayName.fill(RENAMED);
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("status")).toContainText("Profile updated.");

    // No reload, no re-login: the same mounted topbar has to pick this up.
    await expect(account).toContainText(RENAMED);
    // The avatar initials derive from the same value, so they must follow too ("RO", not "DO").
    await expect(account).toContainText("RO");
  } finally {
    // Restore: auth.spec.ts asserts the topbar shows "Dev Owner", and specs share this account.
    await page.goto("/app/settings/profile");
    await page.getByLabel("Display name").fill(ORIGINAL_NAME);
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("status")).toContainText("Profile updated.");
  }
});

/**
 * The three toggles were `<label class="rounded-xl border p-4">` — and a label is display:inline, so
 * the border/padding did not affect layout and the block-level flex span inside fragmented the
 * inline box into thin slivers. A correctly rendered card is far taller than one line of text, so
 * height is the assertion that actually catches the regression.
 */
test("security page renders its toggles as real cards, not inline slivers", async ({ page }) => {
  await page.goto("/app/settings/security");
  await expect(page.getByRole("heading", { name: "Security", level: 1 })).toBeVisible();

  for (const name of ["Enable SAML SSO", "Enable SCIM provisioning", "Require MFA for members"]) {
    const card = page.locator("label").filter({ hasText: name }).first();
    await expect(card).toBeVisible();

    const box = await card.boundingBox();
    expect(box, `${name} card should have a layout box`).not.toBeNull();
    // Inline-label bug produced ~20px (one line box). A padded two-line card is comfortably >60.
    expect(box!.height, `${name} should render as a padded card`).toBeGreaterThan(60);
    // The checkbox and its text sit side by side, so the card spans a real width too.
    expect(box!.width).toBeGreaterThan(400);
  }

  // The checkbox must still be inside its label, so clicking the text toggles it.
  const sso = page.getByRole("checkbox", { name: "Enable SAML SSO" });
  await expect(sso).not.toBeChecked();
  await page.getByText("Enable SAML SSO", { exact: true }).click();
  await expect(sso).toBeChecked();
  await page.getByText("Enable SAML SSO", { exact: true }).click();
  await expect(sso).not.toBeChecked();
});
