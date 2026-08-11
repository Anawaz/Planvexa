import { afterEach, describe, expect, it, vi } from "vitest";
import { getCalendar } from "./client";

function fakeResponse(json: unknown) {
  return {
    status: 200,
    ok: true,
    headers: { get: (name: string) => (name.toLowerCase() === "content-type" ? "application/json" : null) },
    json: async () => json,
    text: async () => "",
  } as unknown as Response;
}

describe("getCalendar", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("does not send a scopeId query param when none is given", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse([]));
    vi.stubGlobal("fetch", fetchMock);

    await getCalendar({ from: "2026-01-01T00:00:00Z", to: "2026-02-01T00:00:00Z" });

    const [url] = fetchMock.mock.calls[0] as [string];
    expect(url).not.toContain("scopeId");
  });

  it("passes a real scopeId through when the caller provides one", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse([]));
    vi.stubGlobal("fetch", fetchMock);

    await getCalendar({
      from: "2026-01-01T00:00:00Z",
      to: "2026-02-01T00:00:00Z",
      scopeId: "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    });

    const [url] = fetchMock.mock.calls[0] as [string];
    expect(url).toContain("scopeId=3fa85f64-5717-4562-b3fc-2c963f66afa6");
  });
});
