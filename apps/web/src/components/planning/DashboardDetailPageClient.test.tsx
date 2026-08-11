import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DashboardDetailPageClient } from "./DashboardDetailPageClient";
import type { Dashboard, Sprint, UpdateDashboardInput } from "@/lib/planning/types";

const getDashboardMock = vi.fn<(id: string) => Promise<Dashboard>>();
const getDashboardDataMock = vi.fn();
const getPortfolioMock = vi.fn();
const getSpaceDrillDownMock = vi.fn();
const listSprintsMock = vi.fn<() => Promise<Sprint[]>>();
const updateDashboardMock = vi.fn<(id: string, input: UpdateDashboardInput) => Promise<Dashboard>>();
const listCustomFieldsMock = vi.fn();
const listScheduledReportsMock = vi.fn();
const createScheduledReportMock = vi.fn();
const setScheduledReportEnabledMock = vi.fn();
const deleteScheduledReportMock = vi.fn();

vi.mock("@/lib/planning/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/planning/client")>();
  return {
    ...actual,
    getDashboard: (id: string) => getDashboardMock(id),
    getDashboardData: () => getDashboardDataMock(),
    getPortfolio: () => getPortfolioMock(),
    getPortfolioPdfHref: () => "/api/v1/reporting/portfolio/export.pdf",
    getSpaceDrillDown: () => getSpaceDrillDownMock(),
    listSprints: () => listSprintsMock(),
    updateDashboard: (id: string, input: UpdateDashboardInput) => updateDashboardMock(id, input),
    listScheduledReports: () => listScheduledReportsMock(),
    createScheduledReport: (input: unknown) => createScheduledReportMock(input),
    setScheduledReportEnabled: (id: string, enabled: boolean) => setScheduledReportEnabledMock(id, enabled),
    deleteScheduledReport: (id: string) => deleteScheduledReportMock(id),
  };
});

vi.mock("@/lib/work/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/work/client")>();
  return { ...actual, recordRecentItem: vi.fn(async () => {}), listCustomFields: () => listCustomFieldsMock() };
});

vi.mock("@/lib/members", () => ({
  useMembers: () => ({
    data: [
      { userId: "user-1", displayName: "Ada Lovelace", email: "ada@planvexa.test", status: "Active" },
      { userId: "user-2", displayName: "No Email", email: null, status: "Active" },
    ],
    isPending: false,
  }),
}));

function dashboardWith(widgets: Dashboard["widgets"]): Dashboard {
  return { id: "dash-1", name: "Ops", isPrivate: false, widgets };
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <DashboardDetailPageClient dashboardId="dash-1" />
    </QueryClientProvider>,
  );
}

describe("DashboardDetailPageClient widget editor", () => {
  beforeEach(() => {
    getDashboardMock.mockReset();
    getDashboardDataMock.mockReset().mockResolvedValue([]);
    getPortfolioMock.mockReset().mockResolvedValue([]);
    getSpaceDrillDownMock.mockReset().mockResolvedValue([]);
    listSprintsMock.mockReset().mockResolvedValue([
      { id: "sprint-1", name: "Sprint Alpha", startUtc: "2026-01-01", endUtc: "2026-01-14", status: "Active", totalPoints: 10 },
    ]);
    updateDashboardMock.mockReset();
    listCustomFieldsMock.mockReset().mockResolvedValue([
      { id: "field-1", name: "Priority tier", type: "Dropdown", scope: "Workspace", isRequired: false, position: 0, options: [] },
    ]);
    listScheduledReportsMock.mockReset().mockResolvedValue([]);
    createScheduledReportMock.mockReset().mockResolvedValue({
      id: "report-1",
      dashboardId: "dash-1",
      recipients: ["ada@planvexa.test"],
      cadence: "Daily",
      isEnabled: true,
      lastSentAtUtc: null,
    });
    setScheduledReportEnabledMock.mockReset();
    deleteScheduledReportMock.mockReset();
  });

  it("adding a widget includes it in the saved PATCH payload", async () => {
    getDashboardMock.mockResolvedValue(
      dashboardWith([{ id: "w1", type: "Overdue", config: { title: "Overdue tasks" } }]),
    );
    updateDashboardMock.mockResolvedValue(
      dashboardWith([
        { id: "w1", type: "Overdue", config: { title: "Overdue tasks" } },
        { id: "w2", type: "TasksByAssignee", config: {} },
      ]),
    );
    const user = userEvent.setup();
    renderPage();

    await waitFor(() => expect(screen.getByText("Ops")).toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText("Widget type"), "TasksByAssignee");
    await user.click(screen.getByRole("button", { name: "Add widget" }));
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(updateDashboardMock).toHaveBeenCalledTimes(1));
    const [, payload] = updateDashboardMock.mock.calls[0];
    expect(payload.widgets).toEqual([
      { type: "Overdue", config: { title: "Overdue tasks" } },
      { type: "TasksByAssignee", config: {} },
    ]);
  });

  it("removing a widget drops it from the saved PATCH payload", async () => {
    getDashboardMock.mockResolvedValue(
      dashboardWith([
        { id: "w1", type: "Overdue", config: { title: "Overdue tasks" } },
        { id: "w2", type: "Completed", config: { title: "Completed tasks" } },
      ]),
    );
    updateDashboardMock.mockResolvedValue(dashboardWith([{ id: "w2", type: "Completed", config: { title: "Completed tasks" } }]));
    const user = userEvent.setup();
    renderPage();

    await waitFor(() => expect(screen.getByText("Ops")).toBeInTheDocument());

    const items = await screen.findAllByRole("listitem");
    const overdueItem = items.find((item) => within(item).queryByText("Overdue"));
    await user.click(within(overdueItem!).getByRole("button", { name: "Remove" }));
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(updateDashboardMock).toHaveBeenCalledTimes(1));
    const [, payload] = updateDashboardMock.mock.calls[0];
    expect(payload.widgets).toEqual([{ type: "Completed", config: { title: "Completed tasks" } }]);
  });

  it("a Burndown widget's sprint config is a picker, never a raw id text input", async () => {
    getDashboardMock.mockResolvedValue(dashboardWith([{ id: "w1", type: "Burndown", config: {} }]));
    renderPage();

    await waitFor(() => expect(screen.getByText("Ops")).toBeInTheDocument());

    const sprintPicker = await screen.findByLabelText("Sprint");
    expect(sprintPicker.tagName).toBe("SELECT");
    expect(screen.queryByLabelText(/sprint/i, { selector: "input" })).not.toBeInTheDocument();

    await waitFor(() => expect(within(sprintPicker as HTMLSelectElement).getByText("Sprint Alpha")).toBeInTheDocument());
  });

  it("a CustomFieldBreakdown widget's field config is a picker, never a raw id text input", async () => {
    getDashboardMock.mockResolvedValue(dashboardWith([{ id: "w1", type: "CustomFieldBreakdown", config: {} }]));
    renderPage();

    await waitFor(() => expect(screen.getByText("Ops")).toBeInTheDocument());

    const fieldPicker = await screen.findByLabelText("Custom field");
    expect(fieldPicker.tagName).toBe("SELECT");
    expect(screen.queryByLabelText(/custom field/i, { selector: "input" })).not.toBeInTheDocument();

    await waitFor(() => expect(within(fieldPicker as HTMLSelectElement).getByText("Priority tier")).toBeInTheDocument());
  });

  it("scheduling a report calls createScheduledReport with the dashboard id, recipient emails and cadence", async () => {
    getDashboardMock.mockResolvedValue(dashboardWith([]));
    const user = userEvent.setup();
    renderPage();

    await waitFor(() => expect(screen.getByText("Ops")).toBeInTheDocument());

    await user.click(screen.getByRole("checkbox", { name: "Ada Lovelace" }));
    await user.selectOptions(screen.getByLabelText("Cadence"), "Weekly");
    await user.click(screen.getByRole("button", { name: "Schedule report" }));

    await waitFor(() => expect(createScheduledReportMock).toHaveBeenCalledTimes(1));
    expect(createScheduledReportMock).toHaveBeenCalledWith({
      dashboardId: "dash-1",
      recipients: ["ada@planvexa.test"],
      cadence: "Weekly",
    });
  });

  it("the scheduled-report recipient picker offers member names/emails to check, never a raw-UUID input", async () => {
    getDashboardMock.mockResolvedValue(dashboardWith([]));
    renderPage();

    await waitFor(() => expect(screen.getByText("Ops")).toBeInTheDocument());

    expect(await screen.findByRole("checkbox", { name: "Ada Lovelace" })).toBeInTheDocument();
    // The member without an email never appears as a recipient option: emails are what the API sends.
    expect(screen.queryByRole("checkbox", { name: "No Email" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/recipient/i, { selector: "input[type=text], input:not([type])" })).not.toBeInTheDocument();
  });
});

describe("DashboardDetailPageClient loading/error/not-found states", () => {
  beforeEach(() => {
    getDashboardMock.mockReset();
    getDashboardDataMock.mockReset().mockResolvedValue([]);
    getPortfolioMock.mockReset().mockResolvedValue([]);
    listScheduledReportsMock.mockReset().mockResolvedValue([]);
  });

  it("shows a genuine error state (not 'Dashboard not found') when the dashboard query rejects", async () => {
    getDashboardMock.mockRejectedValue(new Error("boom"));
    renderPage();

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
    expect(screen.queryByText("Dashboard not found.")).not.toBeInTheDocument();
  });

  it("shows 'Dashboard not found' (not an error) when the query resolves without a dashboard", async () => {
    getDashboardMock.mockResolvedValue(null as never);
    renderPage();

    await waitFor(() => expect(screen.getByText("Dashboard not found.")).toBeInTheDocument());
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows a widget skeleton (not 'No widget data.') while the dashboard data query is still loading", async () => {
    getDashboardMock.mockResolvedValue(dashboardWith([{ id: "w1", type: "Overdue", config: { title: "Overdue tasks" } }]));
    getDashboardDataMock.mockReset().mockReturnValue(new Promise(() => {})); // never resolves
    renderPage();

    await waitFor(() => expect(screen.getByText("Ops")).toBeInTheDocument());
    expect(screen.getByLabelText("Loading widget data")).toBeInTheDocument();
    expect(screen.queryByText("No widget data.")).not.toBeInTheDocument();
  });
});
