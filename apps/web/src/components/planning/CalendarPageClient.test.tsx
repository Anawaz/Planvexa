import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CalendarPageClient } from "./CalendarPageClient";
import { formatLongDate, startOfUtcMonth, startOfUtcWeek } from "./helpers";
import type { CalendarTask } from "@/lib/planning/types";
import type { TimePolicy } from "@/lib/time/types";
import type { Task } from "@/lib/work/types";

const getCalendarMock = vi.fn<() => Promise<CalendarTask[]>>();
const getPolicyMock = vi.fn<() => Promise<TimePolicy>>();
const listMyTasksMock = vi.fn<() => Promise<Task[]>>();

vi.mock("@/lib/planning/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/planning/client")>();
  return { ...actual, getCalendar: () => getCalendarMock() };
});

vi.mock("@/lib/time/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/time/client")>();
  return { ...actual, getPolicy: () => getPolicyMock() };
});

vi.mock("@/lib/work/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/work/client")>();
  return { ...actual, listMyTasks: () => listMyTasksMock() };
});

const basePolicy: TimePolicy = {
  singleActiveTimer: true,
  roundingMinutes: 0,
  minimumDurationSeconds: 0,
  maximumEntrySeconds: 0,
  billableByDefault: false,
  requireDescription: false,
  requireTask: false,
  editWindowHours: 0,
  approvalRequired: false,
  weekStartsOn: 1,
  overtimeThresholdSeconds: 0,
  missingTimeReminderEnabled: false,
  missingTimeReminderCadence: "Daily",
  missingTimeReminderMinimumSeconds: 0,
};

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <CalendarPageClient />
    </QueryClientProvider>,
  );
}

describe("CalendarPageClient", () => {
  beforeEach(() => {
    getCalendarMock.mockResolvedValue([]);
    listMyTasksMock.mockResolvedValue([]);
  });

  it.each([0, 1, 3] as const)(
    "orders the weekday header and grid from the workspace's weekStartsOn policy (%i)",
    async (weekStartsOn) => {
      getPolicyMock.mockResolvedValue({ ...basePolicy, weekStartsOn });
      renderPage();

      const expectedGridStart = startOfUtcWeek(startOfUtcMonth(new Date()), weekStartsOn);
      const expectedFirstLabel = formatLongDate(expectedGridStart);
      const expectedFirstHeader = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"][weekStartsOn];

      await waitFor(() => {
        const headerRow = screen.getByRole("row");
        expect(within(headerRow).getAllByRole("columnheader")[0]).toHaveTextContent(expectedFirstHeader);
      });

      await waitFor(() => {
        expect(screen.getAllByRole("gridcell")[0]).toHaveAttribute("aria-label", expectedFirstLabel);
      });
    },
  );

  it("defaults to a Monday-start grid while the workspace policy hasn't loaded yet", async () => {
    getPolicyMock.mockImplementation(() => new Promise(() => {})); // never resolves
    renderPage();

    const expectedGridStart = startOfUtcWeek(startOfUtcMonth(new Date()), 1);
    const headerRow = await screen.findByRole("row");
    const headers = within(headerRow).getAllByRole("columnheader").map((el) => el.textContent);
    expect(headers[0]).toBe("Mon");

    const gridCells = await screen.findAllByRole("gridcell");
    expect(gridCells[0]).toHaveAttribute("aria-label", formatLongDate(expectedGridStart));
  });
});
