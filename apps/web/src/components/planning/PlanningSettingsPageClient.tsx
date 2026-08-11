"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import { MemberSelect } from "@/components/people/MemberSelect";
import {
  addHoliday,
  addLeave,
  getWorkSchedule,
  listHolidays,
  listLeave,
  removeHoliday,
  removeLeave,
  setWorkSchedule,
} from "@/lib/planning/client";
import { planningKeys } from "@/lib/planning/queries";
import type { WorkSchedule } from "@/lib/planning/types";
import { cn } from "@/lib/utils";
import { dateInputToUtc, formatLongDate, toIsoDateInput } from "./helpers";

const dayOptions = [
  { value: 1, label: "Mon" },
  { value: 2, label: "Tue" },
  { value: 3, label: "Wed" },
  { value: 4, label: "Thu" },
  { value: 5, label: "Fri" },
  { value: 6, label: "Sat" },
  { value: 7, label: "Sun" },
];

const defaultSchedule: WorkSchedule = {
  workingDays: [1, 2, 3, 4, 5],
  dailyCapacityHours: 7.5,
};

export function PlanningSettingsPageClient() {
  const queryClient = useQueryClient();
  const [draftSchedule, setDraftSchedule] = useState<WorkSchedule | null>(null);
  const [holidayName, setHolidayName] = useState("");
  const [holidayDate, setHolidayDate] = useState(() => toIsoDateInput(new Date()));
  // ponytail: raw user id entry until Chunk B adds the member picker.
  const [leaveUserId, setLeaveUserId] = useState("");
  const [leaveStart, setLeaveStart] = useState(() => toIsoDateInput(new Date()));
  const [leaveEnd, setLeaveEnd] = useState(() => toIsoDateInput(new Date()));
  const [leaveType, setLeaveType] = useState("Vacation");
  const scheduleQuery = useQuery({
    queryKey: planningKeys.workSchedule(),
    queryFn: getWorkSchedule,
  });
  const holidaysQuery = useQuery({
    queryKey: planningKeys.holidays(),
    queryFn: listHolidays,
  });
  const leaveQuery = useQuery({
    queryKey: planningKeys.leave(),
    queryFn: () => listLeave(),
  });
  const schedule = draftSchedule ?? scheduleQuery.data ?? defaultSchedule;
  const saveScheduleMutation = useMutation({
    mutationFn: setWorkSchedule,
    onSuccess: (saved) => {
      setDraftSchedule(saved);
      void queryClient.invalidateQueries({ queryKey: planningKeys.workSchedule() });
      void queryClient.invalidateQueries({ queryKey: planningKeys.workloadRoot() });
    },
  });
  const addHolidayMutation = useMutation({
    mutationFn: addHoliday,
    onSuccess: () => {
      setHolidayName("");
      void queryClient.invalidateQueries({ queryKey: planningKeys.holidays() });
      void queryClient.invalidateQueries({ queryKey: planningKeys.workloadRoot() });
    },
  });
  const removeHolidayMutation = useMutation({
    mutationFn: removeHoliday,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: planningKeys.holidays() });
      void queryClient.invalidateQueries({ queryKey: planningKeys.workloadRoot() });
    },
  });
  const addLeaveMutation = useMutation({
    mutationFn: addLeave,
    onSuccess: () => {
      setLeaveType("Vacation");
      void queryClient.invalidateQueries({ queryKey: planningKeys.leaveRoot() });
      void queryClient.invalidateQueries({ queryKey: planningKeys.workloadRoot() });
    },
  });
  const removeLeaveMutation = useMutation({
    mutationFn: removeLeave,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: planningKeys.leaveRoot() });
      void queryClient.invalidateQueries({ queryKey: planningKeys.workloadRoot() });
    },
  });

  function toggleWorkingDay(day: number) {
    setDraftSchedule((current) => {
      const source = current ?? schedule;
      const enabled = source.workingDays.includes(day);
      return {
        ...source,
        workingDays: enabled
          ? source.workingDays.filter((value) => value !== day)
          : [...source.workingDays, day].sort((a, b) => a - b),
      };
    });
  }

  function submitHoliday(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!holidayName.trim()) {
      return;
    }

    addHolidayMutation.mutate({
      name: holidayName.trim(),
      dateUtc: dateInputToUtc(holidayDate),
    });
  }

  function submitLeave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!leaveUserId.trim()) {
      return;
    }

    addLeaveMutation.mutate({
      userId: leaveUserId.trim(),
      startUtc: dateInputToUtc(leaveStart),
      endUtc: dateInputToUtc(leaveEnd),
      type: leaveType.trim() || "Leave",
    });
  }

  return (
    <section aria-labelledby="planning-settings-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Settings</p>
        <h1 id="planning-settings-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Planning settings
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Edit working calendars, holidays, and leave for this workspace.
        </p>
      </div>

      <div className="grid gap-6 xl:grid-cols-2">
        <section
          className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
          aria-labelledby="work-schedule-title"
        >
          <h2 id="work-schedule-title" className="text-sm font-semibold">
            Work schedule
          </h2>
          <p className="mt-1 text-xs text-muted-foreground">
            Working days use 1=Mon through 7=Sun to match the API contract.
          </p>
          <div className="mt-4 grid gap-4">
            <fieldset>
              <legend className="mb-2 text-xs font-medium">Working days</legend>
              <div className="flex flex-wrap gap-2">
                {dayOptions.map((day) => {
                  const active = schedule.workingDays.includes(day.value);
                  return (
                    <button
                      key={day.value}
                      type="button"
                      aria-pressed={active}
                      className={cn(
                        "rounded-full border px-3 py-1.5 text-sm font-medium focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
                        active
                          ? "border-primary bg-primary text-primary-foreground"
                          : "border-border bg-background text-muted-foreground hover:bg-muted",
                      )}
                      onClick={() => toggleWorkingDay(day.value)}
                    >
                      {day.label}
                    </button>
                  );
                })}
              </div>
            </fieldset>
            <label className="grid gap-1 text-xs font-medium">
              Daily capacity hours
              <input
                type="number"
                min={1}
                max={24}
                step={0.25}
                value={schedule.dailyCapacityHours}
                onChange={(event) =>
                  setDraftSchedule((current) => ({
                    ...(current ?? schedule),
                    dailyCapacityHours: Number(event.target.value),
                  }))
                }
                className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              />
            </label>
            <Button
              type="button"
              size="sm"
              disabled={saveScheduleMutation.isPending || schedule.workingDays.length === 0}
              onClick={() => saveScheduleMutation.mutate(schedule)}
            >
              Save schedule
            </Button>
          </div>
        </section>

        <section
          className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
          aria-labelledby="holidays-title"
        >
          <h2 id="holidays-title" className="text-sm font-semibold">
            Holidays
          </h2>
          <form onSubmit={submitHoliday} className="mt-4 grid gap-3 sm:grid-cols-[1fr_1fr_auto]">
            <label className="grid gap-1 text-xs font-medium">
              Date
              <input
                type="date"
                value={holidayDate}
                onChange={(event) => setHolidayDate(event.target.value)}
                className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              />
            </label>
            <label className="grid gap-1 text-xs font-medium">
              Name
              <input
                value={holidayName}
                onChange={(event) => setHolidayName(event.target.value)}
                className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              />
            </label>
            <Button type="submit" size="sm" className="self-end" disabled={addHolidayMutation.isPending}>
              Add
            </Button>
          </form>
          <div className="mt-4 divide-y divide-border rounded-xl border border-border">
            {holidaysQuery.isLoading ? (
              <p className="p-3 text-sm text-muted-foreground">Loading holidays…</p>
            ) : (
              holidaysQuery.data?.map((holiday) => (
                <div key={holiday.id} className="flex items-center justify-between gap-3 p-3">
                  <div>
                    <p className="text-sm font-semibold">{holiday.name}</p>
                    <p className="text-xs text-muted-foreground">{formatLongDate(holiday.dateUtc)}</p>
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={removeHolidayMutation.isPending}
                    onClick={() => removeHolidayMutation.mutate(holiday.id)}
                  >
                    Remove
                  </Button>
                </div>
              ))
            )}
          </div>
        </section>
      </div>

      <section
        className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
        aria-labelledby="leave-title"
      >
        <h2 id="leave-title" className="text-sm font-semibold">
          Leave calendar
        </h2>
        <form onSubmit={submitLeave} className="mt-4 grid gap-3 lg:grid-cols-[1fr_1fr_1fr_1fr_auto]">
          <label className="grid gap-1 text-xs font-medium">
            Teammate
            <MemberSelect
              value={leaveUserId}
              onChange={setLeaveUserId}
              aria-label="Teammate"
            />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Start
            <input
              type="date"
              value={leaveStart}
              onChange={(event) => setLeaveStart(event.target.value)}
              className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            End
            <input
              type="date"
              value={leaveEnd}
              onChange={(event) => setLeaveEnd(event.target.value)}
              className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Type
            <input
              value={leaveType}
              onChange={(event) => setLeaveType(event.target.value)}
              className="h-10 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            />
          </label>
          <Button type="submit" size="sm" className="self-end" disabled={addLeaveMutation.isPending}>
            Add leave
          </Button>
        </form>

        <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {leaveQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">Loading leave…</p>
          ) : (
            leaveQuery.data?.map((entry) => (
              <article
                key={entry.id}
                className="rounded-xl border border-border bg-background p-3 shadow-sm"
              >
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h3 className="text-sm font-semibold">{entry.userId}</h3>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {formatLongDate(entry.startUtc)} – {formatLongDate(entry.endUtc)}
                    </p>
                    <span className="mt-2 inline-flex rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                      {entry.type}
                    </span>
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={removeLeaveMutation.isPending}
                    onClick={() => removeLeaveMutation.mutate(entry.id)}
                  >
                    Remove
                  </Button>
                </div>
              </article>
            ))
          )}
        </div>
      </section>
    </section>
  );
}
