import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { apiClient, ApiError, isMfaRequiredError, proxyHref, setApiContext } from "@/lib/api-client";

type FakeResponseInit = {
  status?: number;
  json?: unknown;
  text?: string;
  headers?: Record<string, string>;
};

function fakeResponse({ status = 200, json, text, headers = {} }: FakeResponseInit) {
  const hasJson = json !== undefined;
  const effectiveHeaders = { ...headers };
  if (hasJson && !Object.keys(effectiveHeaders).some((key) => key.toLowerCase() === "content-type")) {
    effectiveHeaders["content-type"] = "application/json";
  }
  return {
    status,
    ok: status >= 200 && status < 300,
    headers: {
      get: (name: string) => {
        const lower = name.toLowerCase();
        const match = Object.keys(effectiveHeaders).find((key) => key.toLowerCase() === lower);
        return match ? effectiveHeaders[match] : null;
      },
    },
    json: async () => json,
    text: async () => text ?? "",
  } as unknown as Response;
}

describe("api-client", () => {
  beforeEach(() => {
    setApiContext({});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("injects X-Workspace from setApiContext", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse({ status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    setApiContext({ workspaceId: "ws-1" });
    await apiClient.get("/things");

    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Headers).get("X-Workspace")).toBe("ws-1");
  });

  it("omits X-Workspace when noWorkspace is set, even with an ambient workspace", async () => {
    // The host administration console depends on this: /api/v1/host/* is instance-level, and an
    // X-Workspace header would make the API resolve a workspace whose RLS policies then filter every
    // cross-workspace row down to that one. The ambient workspace survives navigation out of /app, so
    // opting out has to be explicit.
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse({ status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    setApiContext({ workspaceId: "ws-1" });
    await apiClient.get("/host/overview", { noWorkspace: true });

    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Headers).has("X-Workspace")).toBe(false);
  });

  it("noWorkspace also overrides an explicitly passed workspaceId", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse({ status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await apiClient.get("/host/overview", { workspaceId: "ws-2", noWorkspace: true });

    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Headers).has("X-Workspace")).toBe(false);
  });

  it("lets a per-call workspace override the ambient context", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse({ status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    setApiContext({ workspaceId: "ws-1" });
    await apiClient.get("/things", { workspaceId: "ws-2" });

    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Headers).get("X-Workspace")).toBe("ws-2");
  });

  it("sets the Idempotency-Key header when provided", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse({ status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await apiClient.post("/things", { name: "a" }, { idempotencyKey: "key-1" });

    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Headers).get("Idempotency-Key")).toBe("key-1");
  });

  it("sets Content-Type for a JSON body but not for FormData", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse({ status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await apiClient.post("/things", { name: "a" });
    const [, jsonInit] = fetchMock.mock.calls[0];
    expect((jsonInit.headers as Headers).get("Content-Type")).toBe("application/json");
    expect(jsonInit.body).toBe(JSON.stringify({ name: "a" }));

    const form = new FormData();
    form.set("name", "a");
    await apiClient.post("/things", form);
    const [, formInit] = fetchMock.mock.calls[1];
    expect((formInit.headers as Headers).get("Content-Type")).toBeNull();
    expect(formInit.body).toBe(form);
  });

  it("strips the /api/v1/ prefix and leading slashes when building the URL", async () => {
    const fetchMock = vi.fn().mockResolvedValue(fakeResponse({ status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await apiClient.get("/api/v1/things/1");
    expect(fetchMock.mock.calls[0][0]).toBe("/api/proxy/things/1");

    await apiClient.get("things/2");
    expect(fetchMock.mock.calls[1][0]).toBe("/api/proxy/things/2");
  });

  it("returns undefined for a 204 response", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(fakeResponse({ status: 204 })));
    await expect(apiClient.delete("/things/1")).resolves.toBeUndefined();
  });

  it("maps a failed response to an ApiError with title, status, and correlation id", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        fakeResponse({
          status: 422,
          json: { title: "Validation failed", errors: {} },
          headers: { "X-Correlation-Id": "corr-123" },
        }),
      ),
    );

    const error = (await apiClient.get("/things/1").catch((caught: unknown) => caught)) as ApiError;
    expect(error).toBeInstanceOf(ApiError);
    expect(error.message).toBe("Validation failed");
    expect(error.status).toBe(422);
    expect(error.correlationId).toBe("corr-123");
    expect(error.details).toEqual({ title: "Validation failed", errors: {} });
  });

  it("prefers the ProblemDetails detail over the generic title", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        fakeResponse({
          status: 400,
          json: { title: "Bad Request", detail: "ClickUp import is not yet implemented." },
        }),
      ),
    );

    const error = (await apiClient.get("/things/1").catch((caught: unknown) => caught)) as ApiError;
    expect(error.message).toBe("ClickUp import is not yet implemented.");
  });

  it("falls back to a generic message when the error body has no title", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(fakeResponse({ status: 500, text: "" })));
    const error = (await apiClient.get("/things/1").catch((caught: unknown) => caught)) as ApiError;
    expect(error.message).toBe("Request failed with 500");
  });

  it("builds a proxy href with workspace query aliases and extra params", () => {
    setApiContext({ workspaceId: "ws-1" });
    const href = proxyHref("/exports/tasks.csv", { view: "board" });
    const url = new URL(href, "http://proxy.local");
    expect(url.pathname).toBe("/api/proxy/exports/tasks.csv");
    expect(url.searchParams.get("view")).toBe("board");
    expect(url.searchParams.get("x-workspace")).toBe("ws-1");
  });

  it("omits query aliases when there is no ambient context", () => {
    const href = proxyHref("/exports/tasks.csv");
    const url = new URL(href, "http://proxy.local");
    expect(url.searchParams.get("x-workspace")).toBeNull();
  });
});

describe("isMfaRequiredError", () => {
  it("recognizes the mfa-required problem type", () => {
    const error = new ApiError("Forbidden", 403, { type: "https://planvexa.dev/problems/mfa-required" });
    expect(isMfaRequiredError(error)).toBe(true);
  });

  it("does not mistake a plain 403 (e.g. not a workspace member) for the mfa-required case", () => {
    const error = new ApiError("Forbidden", 403, { type: "https://httpstatuses.io/403" });
    expect(isMfaRequiredError(error)).toBe(false);
  });

  it("returns false for a non-ApiError and for a missing details body", () => {
    expect(isMfaRequiredError(new Error("boom"))).toBe(false);
    expect(isMfaRequiredError(new ApiError("Forbidden", 403, undefined))).toBe(false);
  });
});
