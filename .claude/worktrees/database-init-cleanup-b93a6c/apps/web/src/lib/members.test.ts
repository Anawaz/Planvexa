import { describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import { useQuery } from "@tanstack/react-query";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useMemberDirectory, type Member } from "@/lib/members";

vi.mock("@tanstack/react-query", () => ({ useQuery: vi.fn() }));
vi.mock("@/lib/app-context/AppContext", () => ({ useAppContext: vi.fn() }));

const members: Member[] = [
  { id: "m1", userId: "user-1", role: "member", status: "active", isGuest: false, joinedAtUtc: "", displayName: "Ada Lovelace", email: "ada@example.com" },
  { id: "m2", userId: "user-2", role: "member", status: "active", isGuest: false, joinedAtUtc: "", displayName: null, email: "grace@example.com" },
];

function mockData(data: Member[] | undefined) {
  vi.mocked(useAppContext).mockReturnValue({ workspaceId: "ws-1" } as unknown as ReturnType<typeof useAppContext>);
  vi.mocked(useQuery).mockReturnValue({ data } as unknown as ReturnType<typeof useQuery>);
}

describe("useMemberDirectory", () => {
  it("uses the member's display name when present", () => {
    mockData(members);
    const { result } = renderHook(() => useMemberDirectory());
    expect(result.current.getLabel("user-1")).toBe("Ada Lovelace");
    expect(result.current.getInitials("user-1")).toBe("AL");
  });

  it("falls back to email when there is no display name", () => {
    mockData(members);
    const { result } = renderHook(() => useMemberDirectory());
    expect(result.current.getLabel("user-2")).toBe("grace@example.com");
    expect(result.current.getInitials("user-2")).toBe("G");
  });

  it("falls back to the raw id for an unknown member", () => {
    mockData(members);
    const { result } = renderHook(() => useMemberDirectory());
    expect(result.current.getLabel("user-404")).toBe("user-404");
    expect(result.current.getInitials("user-404")).toBe("US");
  });

  it("falls back to the raw id when the member list has not loaded yet", () => {
    mockData(undefined);
    const { result } = renderHook(() => useMemberDirectory());
    expect(result.current.getLabel("user-1")).toBe("user-1");
    expect(result.current.getInitials("user-1")).toBe("US");
  });
});
