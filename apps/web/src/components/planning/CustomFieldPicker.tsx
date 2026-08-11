"use client";

import { useQuery } from "@tanstack/react-query";
import { listCustomFields } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";

type CustomFieldPickerProps = {
  id?: string;
  value: string;
  onChange: (customFieldId: string) => void;
  disabled?: boolean;
};

// Types the CustomFieldBreakdown widget can group by: single-valued and stored (unlike MultiSelect,
// which can put one task in several buckets, and Formula/Rollup/Relationship, which never have a
// stored CustomFieldValue row) -- see WorkReportingQueries.CustomFieldValueCountsAsync's doc comment.
const GROUPABLE_TYPES = new Set([
  "Text", "LongText", "Number", "Currency", "Boolean", "Date", "DateTime",
  "Dropdown", "Url", "Email", "Rating", "User", "Team", "Phone", "Location", "Progress",
]);

/**
 * Dropdown over the workspace's custom fields, keyed by id but always chosen by name — same
 * "no raw-UUID entry" convention as SprintPicker.
 */
export function CustomFieldPicker({ id, value, onChange, disabled }: CustomFieldPickerProps) {
  const { data: fields, isLoading } = useQuery({
    queryKey: workKeys.customFields(),
    queryFn: listCustomFields,
  });
  const groupable = fields?.filter((field) => GROUPABLE_TYPES.has(field.type)) ?? [];

  return (
    <select
      id={id}
      value={value}
      disabled={disabled || isLoading}
      onChange={(event) => onChange(event.target.value)}
      className="h-9 w-full rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    >
      <option value="">{isLoading ? "Loading custom fields…" : "Select a custom field…"}</option>
      {groupable.map((field) => (
        <option key={field.id} value={field.id}>
          {field.name}
        </option>
      ))}
    </select>
  );
}
