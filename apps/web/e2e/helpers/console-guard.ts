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
export const test = base.extend<{ consoleAllowlist: string[]; consoleGuard: void }>({
  // Regex *source strings*, not RegExp instances: test.use() only overrides { option: true }
  // fixtures, and those are deep-merged across test.use() calls -- RegExp instances have no own
  // enumerable properties, so merging one into the default silently produces {} instead of the
  // pattern. Plain strings survive the merge; compiled to RegExp below, right before use.
  consoleAllowlist: [[], { option: true }],
  consoleGuard: [
    async ({ page, consoleAllowlist }, use) => {
      const errors: string[] = [];
      const allowed = [...allowlist, ...consoleAllowlist.map((pattern) => new RegExp(pattern))];

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
