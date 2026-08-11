/**
 * Free-text duration entry.
 *
 * Grammar (case-insensitive, whitespace is ignored everywhere):
 *   - unit list, largest to smallest, each unit at most once and in order:
 *     `1h 45m 44s`, `1h45m44s`, `1h 45m`, `90m`, `45s`, `1.5h`
 *   - a trailing bare number takes the next smaller unit: `2h30` = 2h 30m,
 *     `2h30m15` = 2h 30m 15s. Nothing is smaller than seconds, so `30s5` is invalid.
 *   - a bare number on its own is MINUTES: `90` = 90m — the convention every comparable
 *     duration field uses, so muscle memory carries over.
 *   - colon form: `2:30` = 2h 30m, `1:45:44` = 1h 45m 44s.
 *
 * Anything else is invalid. Fractions round to the nearest second. The 24h cap is a client-side
 * guard only — the server's `TimePolicy.maximumEntrySeconds` stays authoritative.
 */

export const MAX_DURATION_SECONDS = 24 * 60 * 60;

export type DurationParseError = "empty" | "invalid" | "too-long";

export type DurationParseResult = { seconds: number } | { error: DurationParseError };

const UNITS = [
  { pattern: /^h(?:rs?|ours?)?/, seconds: 3600 },
  { pattern: /^m(?:in(?:ute)?s?)?/, seconds: 60 },
  { pattern: /^s(?:ec(?:ond)?s?)?/, seconds: 1 },
];

const BARE_UNIT_INDEX = 1; // a lone number means minutes
const NUMBER = /^\d+(?:\.\d+)?/;
const COLON_FORM = /^\d{1,2}(?::[0-5]?\d){1,2}$/;

export function parseDuration(input: string): DurationParseResult {
  const compact = input.replace(/\s+/g, "").toLowerCase();
  if (!compact) {
    return { error: "empty" };
  }

  const total = COLON_FORM.test(compact) ? parseColonForm(compact) : parseUnitForm(compact);
  if (total === null) {
    return { error: "invalid" };
  }

  const seconds = Math.round(total);
  return seconds > MAX_DURATION_SECONDS ? { error: "too-long" } : { seconds };
}

/** `2:30` is h:mm, `1:45:44` is h:mm:ss. */
function parseColonForm(compact: string) {
  const parts = compact.split(":").map(Number);
  const [hours, minutes, seconds] = parts.length === 2 ? [...parts, 0] : parts;
  return hours * 3600 + minutes * 60 + seconds;
}

function parseUnitForm(compact: string) {
  let rest = compact;
  let total = 0;
  let previousUnit = -1;

  while (rest) {
    const number = NUMBER.exec(rest);
    if (!number) {
      return null;
    }
    rest = rest.slice(number[0].length);

    let unit = UNITS.findIndex((candidate) => candidate.pattern.test(rest));
    if (unit === -1) {
      // Bare number: only legal as the final token, and it means the next smaller unit.
      if (rest) {
        return null;
      }
      unit = previousUnit === -1 ? BARE_UNIT_INDEX : previousUnit + 1;
      if (unit >= UNITS.length) {
        return null;
      }
    } else {
      rest = rest.replace(UNITS[unit].pattern, "");
    }

    if (unit <= previousUnit) {
      return null; // repeated or out-of-order unit
    }

    total += Number(number[0]) * UNITS[unit].seconds;
    previousUnit = unit;
  }

  return total;
}

/**
 * Canonical `1h 45m 44s`, dropping zero units (`45m`, `2h`, `30s`), `0m` for nothing.
 * `formatDuration` in ./format.ts stays the H:MM clock reading used in entry lists.
 */
export function formatDurationText(totalSeconds: number) {
  const safe = Math.max(0, Math.round(totalSeconds));
  const parts: string[] = [];
  const hours = Math.floor(safe / 3600);
  const minutes = Math.floor((safe % 3600) / 60);
  const seconds = safe % 60;

  if (hours) parts.push(`${hours}h`);
  if (minutes) parts.push(`${minutes}m`);
  if (seconds) parts.push(`${seconds}s`);

  return parts.join(" ") || "0m";
}
