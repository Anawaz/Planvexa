import { describe, expect, it } from "vitest";
import { shiftDateIso } from "./helpers";

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
