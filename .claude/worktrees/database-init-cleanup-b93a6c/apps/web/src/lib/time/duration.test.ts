import { describe, expect, it } from "vitest";
import { MAX_DURATION_SECONDS, formatDurationText, parseDuration } from "./duration";

function seconds(input: string) {
  const result = parseDuration(input);
  return "seconds" in result ? result.seconds : result.error;
}

describe("parseDuration", () => {
  it.each<[string, number]>([
    ["1h 45m 44s", 6344],
    ["1h45m44s", 6344],
    ["1H45M44S", 6344],
    ["  1h   45m  ", 6300],
    ["1 hour 45 minutes 44 seconds", 6344],
    ["2hrs 5mins 3secs", 7503],
    ["90m", 5400],
    ["45s", 45],
    ["2h", 7200],
    ["1.5h", 5400],
    ["0.5m", 30],
  ])("parses %s", (input, expected) => {
    expect(seconds(input)).toBe(expected);
  });

  it("treats a trailing bare number as the next smaller unit", () => {
    expect(seconds("2h30")).toBe(2 * 3600 + 30 * 60);
    expect(seconds("2h30m15")).toBe(2 * 3600 + 30 * 60 + 15);
    expect(seconds("45m30")).toBe(45 * 60 + 30);
  });

  it("treats a bare number as minutes", () => {
    expect(seconds("90")).toBe(5400);
    expect(seconds("0")).toBe(0);
  });

  it("parses the colon form", () => {
    expect(seconds("2:30")).toBe(2 * 3600 + 30 * 60);
    expect(seconds("1:45:44")).toBe(6344);
    expect(seconds("0:05")).toBe(300);
  });

  it("rounds fractions to the nearest second", () => {
    expect(seconds("1.7s")).toBe(2);
    expect(seconds("0.005m")).toBe(0);
    expect(seconds("1.005m")).toBe(60);
  });

  it("reports empty separately from invalid", () => {
    expect(parseDuration("")).toEqual({ error: "empty" });
    expect(parseDuration("   ")).toEqual({ error: "empty" });
  });

  it.each(["abc", "1x", "-5", "-1h", "h", "m30", "1h 2x", "1:75", "::", "1..5h"])(
    "rejects %s",
    (input) => {
      expect(parseDuration(input)).toEqual({ error: "invalid" });
    },
  );

  it("rejects repeated, out-of-order, or over-small units", () => {
    expect(parseDuration("1h 1h")).toEqual({ error: "invalid" });
    expect(parseDuration("30m 1h")).toEqual({ error: "invalid" });
    expect(parseDuration("30s5")).toEqual({ error: "invalid" });
  });

  it("caps at 24 hours", () => {
    expect(seconds("24h")).toBe(MAX_DURATION_SECONDS);
    expect(parseDuration("24h 1s")).toEqual({ error: "too-long" });
    expect(parseDuration("2000m")).toEqual({ error: "too-long" });
  });
});

describe("formatDurationText", () => {
  it.each<[number, string]>([
    [6344, "1h 45m 44s"],
    [2700, "45m"],
    [7200, "2h"],
    [30, "30s"],
    [3630, "1h 30s"],
    [0, "0m"],
    [-10, "0m"],
    [59.6, "1m"],
  ])("formats %s as %s", (input, expected) => {
    expect(formatDurationText(input)).toBe(expected);
  });
});

describe("round trip", () => {
  it.each(["1h 45m 44s", "45m", "2h", "30s", "1h 30s", "0m"])("re-parses %s", (text) => {
    const parsed = parseDuration(text);
    expect(parsed).toHaveProperty("seconds");
    expect(formatDurationText((parsed as { seconds: number }).seconds)).toBe(text);
  });

  it("canonicalises loose input", () => {
    expect(formatDurationText(seconds("2h30") as number)).toBe("2h 30m");
    expect(formatDurationText(seconds("1:45:44") as number)).toBe("1h 45m 44s");
    expect(formatDurationText(seconds("90") as number)).toBe("1h 30m");
  });
});
