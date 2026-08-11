import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("next/headers", () => ({
  cookies: vi.fn(),
}));

import {
  createSessionCookies,
  decryptSession,
  encryptSession,
  expiredSessionCookies,
  sessionCookieName,
  type SessionPayload,
} from "@/lib/auth/session";

function makeSession(overrides: Partial<SessionPayload> = {}): SessionPayload {
  return {
    accessToken: "access-token",
    refreshToken: "refresh-token",
    idToken: "id-token",
    expiresAt: Date.now() + 60_000,
    refreshExpiresAt: Date.now() + 3_600_000,
    user: { subject: "user-1", email: "user@example.com", name: "User One" },
    ...overrides,
  };
}

describe("session", () => {
  beforeEach(() => {
    process.env.PLANVEXA_WEB_SESSION_SECRET = "test-secret";
  });

  it("round-trips a session through encrypt/decrypt", () => {
    const session = makeSession();
    const decrypted = decryptSession(encryptSession(session));
    expect(decrypted).toEqual(session);
  });

  it("returns null once refreshExpiresAt is in the past", () => {
    const session = makeSession({ refreshExpiresAt: Date.now() - 1000 });
    expect(decryptSession(encryptSession(session))).toBeNull();
  });

  it("returns null for tampered ciphertext", () => {
    const value = encryptSession(makeSession());
    const [iv, tag, ciphertext] = value.split(".");
    const tampered = [iv, tag, `${ciphertext.slice(0, -2)}${ciphertext.slice(-2) === "AA" ? "BB" : "AA"}`].join(".");
    expect(decryptSession(tampered)).toBeNull();
  });

  it("returns null for a malformed value", () => {
    expect(decryptSession("not-a-valid-session-value")).toBeNull();
  });

  it("chunks a large payload across .0/.1 and leaves unused chunks expired and empty", () => {
    const session = makeSession({ accessToken: "x".repeat(4000) });
    const descriptors = createSessionCookies(session);

    expect(descriptors).toHaveLength(4);
    expect(descriptors[0].name).toBe(`${sessionCookieName}.0`);
    expect(descriptors[1].name).toBe(`${sessionCookieName}.1`);
    expect(descriptors[0].value.length).toBe(3500);
    expect(descriptors[1].value.length).toBeGreaterThan(0);
    expect(descriptors[0].expires.getTime()).toBe(session.refreshExpiresAt);
    expect(descriptors[1].expires.getTime()).toBe(session.refreshExpiresAt);

    expect(descriptors[2].value).toBe("");
    expect(descriptors[3].value).toBe("");
    expect(descriptors[2].expires.getTime()).toBe(0);
    expect(descriptors[3].expires.getTime()).toBe(0);

    const combined = descriptors.map((descriptor) => descriptor.value).join("");
    expect(decryptSession(combined)).toEqual(session);
  });

  it("throws when the encrypted payload exceeds the maximum cookie storage", () => {
    const session = makeSession({ accessToken: "x".repeat(20_000) });
    expect(() => createSessionCookies(session)).toThrow();
  });

  it("expires all session cookies including the legacy unchunked name", () => {
    const descriptors = expiredSessionCookies();
    const names = descriptors.map((descriptor) => descriptor.name);
    expect(names).toEqual([
      sessionCookieName,
      `${sessionCookieName}.0`,
      `${sessionCookieName}.1`,
      `${sessionCookieName}.2`,
      `${sessionCookieName}.3`,
    ]);
    for (const descriptor of descriptors) {
      expect(descriptor.value).toBe("");
      expect(descriptor.expires.getTime()).toBe(0);
    }
  });
});
