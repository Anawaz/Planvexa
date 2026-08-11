// SPDX-FileCopyrightText: 2026 Planvexa contributors
// SPDX-License-Identifier: AGPL-3.0-only

import { expect, test } from "./helpers/console-guard";

test.describe("legal page", () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test("/legal is publicly accessible and shows source-code information", async ({ page }) => {
    await page.goto("/legal");

    await expect(page.getByRole("heading", { name: "Planvexa" })).toBeVisible();
    await expect(page.getByText("Official Planvexa distribution")).toBeVisible();
    await expect(page.getByText("AGPL-3.0-only")).toBeVisible();
    await expect(page.getByRole("link", { name: "GNU Affero General Public License, Version 3 only" })).toBeVisible();
  });
});