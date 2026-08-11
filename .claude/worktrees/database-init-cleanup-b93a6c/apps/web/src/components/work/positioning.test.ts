import { describe, expect, it } from "vitest";
import { dropPosition, midpoint } from "./positioning";

// Ported from positioning.check.mjs (kept alongside as the node --experimental-strip-types smoke check).
describe("positioning", () => {
  const column = [
    { id: "a", position: 1000 },
    { id: "b", position: 2000 },
    { id: "c", position: 3000 },
  ];

  it("drags down onto b lands between b and c", () => {
    expect(dropPosition(column, "a", "b")).toBe(2500);
  });

  it("drags up onto b lands between a and b", () => {
    expect(dropPosition(column, "c", "b")).toBe(1500);
  });

  it("drags up onto the first card lands before it", () => {
    expect(dropPosition(column, "c", "a")).toBe(1000 - 1024);
  });

  it("appends when the drop area is empty", () => {
    expect(dropPosition(column, "a")).toBe(3000 + 1024);
  });

  it("returns the base step for an empty column", () => {
    expect(dropPosition([], "x")).toBe(1024);
  });

  it("inserts before the target on a cross-column drop", () => {
    expect(dropPosition(column, "x", "a")).toBe(1000 - 1024);
  });

  it("midpoint with no neighbours returns the base step", () => {
    expect(midpoint(undefined, undefined)).toBe(1024);
  });

  it("midpoint with only a predecessor adds the step", () => {
    expect(midpoint(1000, undefined)).toBe(1000 + 1024);
  });

  it("midpoint with only a successor subtracts the step", () => {
    expect(midpoint(undefined, 1000)).toBe(1000 - 1024);
  });

  it("midpoint between two neighbours averages them", () => {
    expect(midpoint(1000, 2000)).toBe(1500);
  });
});
