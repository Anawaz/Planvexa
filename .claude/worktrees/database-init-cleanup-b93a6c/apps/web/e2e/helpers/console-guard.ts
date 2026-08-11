import { expect, test as base } from "@playwright/test";

/**
 * Console noise that is not a product defect. Keep this empty unless a message is both observed
 * and justified — an entry here is a permanently muted browser error.
 */
const allowlist: RegExp[] = [
  // Pre-auth pages (/login) still mount the app shell providers, which probe /api/session/me.
];

/**
 * Every test importing this `test` fails if the page logged an error or threw.
 *
 * A spec that deliberately provokes failed requests (workspace isolation, offline behaviour) widens
 * the guard for itself with `test.use({ consoleAllowlist: [/…/] })` rather than muting the
 * message for the whole suite.
 */
export const test = base.extend<{ consoleAllowlist: RegExp[]; consoleGuard: void }>({
  consoleAllowlist: [[], { option: true }],
  consoleGuard: [
    async ({ page, consoleAllowlist }, use) => {
      const errors: string[] = [];
      const allowed = [...allowlist, ...consoleAllowlist];

      page.on("console", (message) => {
        if (message.type() === "error") {
          errors.push(message.text());
        }
      });
      page.on("pageerror", (error) => errors.push(`pageerror: ${error.message}`));

      await use();

      expect(errors.filter((error) => !allowed.some((pattern) => pattern.test(error)))).toEqual([]);
    },
    { auto: true },
  ],
});

export { expect };
