import { describe, expect, it } from "vitest";
import { toUtcInstant } from "./client";

describe("toUtcInstant", () => {
  it("pins a date-only value to midnight UTC", () => {
    expect(toUtcInstant("2026-09-01")).toBe("2026-09-01T00:00:00Z");
  });

  it("leaves a value that already carries a time alone", () => {
    expect(toUtcInstant("2026-09-01T08:30:00Z")).toBe("2026-09-01T08:30:00Z");
  });

  it("passes empty values straight through", () => {
    expect(toUtcInstant(undefined)).toBeUndefined();
    expect(toUtcInstant("")).toBe("");
  });
});
