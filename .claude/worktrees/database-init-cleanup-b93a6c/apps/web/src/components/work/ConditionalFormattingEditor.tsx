"use client";

import { Button } from "@/components/ui/Button";
import type { ConditionalFormattingRule, FilterFieldName, FilterOperatorName } from "@/lib/work/types";

const FIELD_OPTIONS: Array<[FilterFieldName, string]> = [
  ["status", "Status"],
  ["assignee", "Assignee"],
  ["tag", "Tag"],
  ["priority", "Priority"],
  ["title", "Title"],
  ["duedate", "Due date"],
  ["startdate", "Start date"],
  ["iscompleted", "Completed"],
];

const OPERATOR_OPTIONS: FilterOperatorName[] = ["Equals", "NotEquals", "Contains", "IsEmpty", "IsNotEmpty", "GreaterThan", "LessThan"];

const COLOR_SWATCHES = ["#ef4444", "#f59e0b", "#22c55e", "#3b82f6", "#8b5cf6", "#ec4899"];

const fieldClassName =
  "h-8 rounded-md border border-border bg-background px-2 text-xs font-normal text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

function newRule(): ConditionalFormattingRule {
  return {
    id: crypto.randomUUID(),
    field: "priority",
    operator: "Equals",
    value: "Urgent",
    color: COLOR_SWATCHES[0],
    style: "row",
  };
}

/**  . uc(")if field X matches condition Y, apply style Z" -- badge color or row highlight, stored per
 * saved view (SavedView.configJson, no schema change needed). Applied in TableView. */
export function ConditionalFormattingEditor({
  rules,
  onChange,
}: {
  rules: ConditionalFormattingRule[];
  onChange: (next: ConditionalFormattingRule[]) => void;
}) {
  function update(id: string, patch: Partial<ConditionalFormattingRule>) {
    onChange(rules.map((rule) => (rule.id === id ? { ...rule, ...patch } : rule)));
  }

  function remove(id: string) {
    onChange(rules.filter((rule) => rule.id !== id));
  }

  return (
    <div className="space-y-3 rounded-lg border border-border bg-background p-3">
      {rules.length === 0 ? (
        <p className="text-xs text-muted-foreground">
          No rules yet. Add one to highlight rows or badges when a condition matches.
        </p>
      ) : null}
      {rules.map((rule) => (
        <div key={rule.id} className="flex flex-wrap items-center gap-2">
          <span className="text-xs text-muted-foreground">If</span>
          <select
            className={fieldClassName}
            value={rule.field}
            onChange={(e) => update(rule.id, { field: e.target.value as FilterFieldName })}
          >
            {FIELD_OPTIONS.map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
          <select
            className={fieldClassName}
            value={rule.operator}
            onChange={(e) => update(rule.id, { operator: e.target.value as FilterOperatorName })}
          >
            {OPERATOR_OPTIONS.map((operator) => (
              <option key={operator} value={operator}>
                {operator}
              </option>
            ))}
          </select>
          {rule.operator !== "IsEmpty" && rule.operator !== "IsNotEmpty" ? (
            <input
              className={fieldClassName}
              value={rule.value ?? ""}
              placeholder="value"
              onChange={(e) => update(rule.id, { value: e.target.value })}
            />
          ) : null}
          <span className="text-xs text-muted-foreground">apply</span>
          <select
            className={fieldClassName}
            value={rule.style}
            onChange={(e) => update(rule.id, { style: e.target.value as "row" | "badge" })}
          >
            <option value="row">row highlight</option>
            <option value="badge">title badge</option>
          </select>
          <div className="flex items-center gap-1">
            {COLOR_SWATCHES.map((color) => (
              <button
                key={color}
                type="button"
                aria-label={`Use color ${color}`}
                aria-pressed={rule.color === color}
                className="size-5 rounded-full border border-border ring-offset-2 focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
                style={{ backgroundColor: color, outline: rule.color === color ? `2px solid ${color}` : undefined }}
                onClick={() => update(rule.id, { color })}
              />
            ))}
          </div>
          <Button type="button" variant="ghost" size="sm" onClick={() => remove(rule.id)} aria-label="Remove rule">
            ✕
          </Button>
        </div>
      ))}
      <Button type="button" variant="outline" size="sm" onClick={() => onChange([...rules, newRule()])}>
        + Rule
      </Button>
    </div>
  );
}
