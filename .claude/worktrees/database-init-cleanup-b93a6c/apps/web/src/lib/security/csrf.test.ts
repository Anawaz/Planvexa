import { describe, expect, it } from "vitest";
import { NextRequest } from "next/server";

import { csrfRejection } from "@/lib/security/csrf";

function request(method: string, headers: Record<string, string> = {}) {
  return new NextRequest("https://app.planvexa.example/api/proxy/workspaces", {
    method,
    headers,
  });
}

describe("csrfRejection", () => {
  it("never blocks safe methods, regardless of headers", () => {
    expect(csrfRejection(request("GET"))).toBeNull();
    expect(csrfRejection(request("HEAD"))).toBeNull();
  });

  it("allows a same-origin mutating request (Sec-Fetch-Site: same-origin)", () => {
    expect(csrfRejection(request("POST", { "sec-fetch-site": "same-origin" }))).toBeNull();
  });

  it("allows a mutating request with no referring page (Sec-Fetch-Site: none)", () => {
    expect(csrfRejection(request("POST", { "sec-fetch-site": "none" }))).toBeNull();
  });

  it("blocks a cross-site mutating request (Sec-Fetch-Site: cross-site)", async () => {
    const response = csrfRejection(request("POST", { "sec-fetch-site": "cross-site" }));
    expect(response).not.toBeNull();
    expect(response!.status).toBe(403);
  });

  it("blocks a same-site (but not same-origin) mutating request", async () => {
    const response = csrfRejection(request("DELETE", { "sec-fetch-site": "same-site" }));
    expect(response).not.toBeNull();
    expect(response!.status).toBe(403);
  });

  it("falls back to Origin comparison when Sec-Fetch-Site is absent, and allows a matching origin", () => {
    expect(csrfRejection(request("PUT", { origin: "https://app.planvexa.example" }))).toBeNull();
  });

  it("falls back to Origin comparison and blocks a mismatched origin", async () => {
    const response = csrfRejection(request("PATCH", { origin: "https://evil.example" }));
    expect(response).not.toBeNull();
    expect(response!.status).toBe(403);
  });

  it("blocks a mutating request with neither header", async () => {
    const response = csrfRejection(request("POST"));
    expect(response).not.toBeNull();
    expect(response!.status).toBe(403);
  });
});
