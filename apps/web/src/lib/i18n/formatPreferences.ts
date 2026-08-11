/**
 * The signed-in user's saved display preferences (Timezone/Locale on User — see AppContext.tsx),
 * exposed as a module-level singleton so every existing Intl.DateTimeFormat/NumberFormat call site
 * can read it as its default without threading it through every component's props. Same
 * synchronous-during-render-update pattern as api-client's setApiContext.
 *
 * `undefined` for either field means "no preference set" — passing `undefined` to the Intl
 * constructors falls back to the browser's ambient locale/timezone, exactly as before this existed.
 */
export type FormatPreferences = { locale?: string; timeZone?: string };

let preferences: FormatPreferences = {};

export function setFormatPreferences(next: FormatPreferences) {
  preferences = next;
}

export function getFormatPreferences(): FormatPreferences {
  return preferences;
}
