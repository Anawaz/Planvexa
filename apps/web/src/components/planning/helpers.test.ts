import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { setFormatPreferences } from "@/lib/i18n/formatPreferences";
import { dateInputToUtc, formatLongDate, formatShortDate, shiftDateIso } from "./helpers";

describe("shiftDateIso", () => {
  it("shifts a date forward and backward by whole days", () => {
    expect(shiftDateIso("2026-08-04T00:00:00.000Z", 3)).toBe("2026-08-07T00:00:00.000Z");
    expect(shiftDateIso("2026-08-04T00:00:00.000Z", -2)).toBe("2026-08-02T00:00:00.000Z");
  });

  it("crosses month boundaries", () => {
    expect(shiftDateIso("2026-08-31T00:00:00.000Z", 1)).toBe("2026-09-01T00:00:00.000Z");
  });

  it("preserves the time component while shifting the day", () => {
    expect(shiftDateIso("2026-08-04T09:30:00.000Z", 1)).toBe("2026-08-05T09:30:00.000Z");
  });

  it("returns null for missing input", () => {
    expect(shiftDateIso(null, 5)).toBeNull();
    expect(shiftDateIso(undefined, 5)).toBeNull();
  });
});

describe("formatShortDate / formatLongDate", () => {
  // A date-only value has no meaningful time-of-day: dateInputToUtc stores it as UTC midnight,
  // so it must always render as the same calendar day regardless of the viewer's local timezone.
  const originalTz = process.env.TZ;

  beforeEach(() => {
    process.env.TZ = "America/Los_Angeles"; // UTC-7/-8, i.e. a negative offset
  });

  afterEach(() => {
    process.env.TZ = originalTz;
  });

  it("renders the input calendar day even in a negative-UTC-offset timezone", () => {
    const stored = dateInputToUtc("2026-08-10");

    expect(formatShortDate(stored)).toBe("Aug 10");
    expect(formatLongDate(stored)).toBe("August 10, 2026");
  });

  it("honors an explicit locale override", () => {
    const stored = dateInputToUtc("2026-08-10");

    expect(formatShortDate(stored, "de-DE")).toBe("10. Aug.");
    expect(formatLongDate(stored, "de-DE")).toBe("10. August 2026");
  });

  it("falls back to the user's saved locale preference when no override is passed", () => {
    setFormatPreferences({ locale: "de-DE" });
    try {
      const stored = dateInputToUtc("2026-08-10");
      expect(formatShortDate(stored)).toBe("10. Aug.");
    } finally {
      setFormatPreferences({});
    }
  });
});
