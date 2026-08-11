"use client";

import { Button } from "@/components/ui/Button";
import type {
  FilterCondition,
  FilterFieldName,
  FilterGroup,
  FilterOperatorName,
  StatusDefinition,
  Tag,
} from "@/lib/work/types";
import type { Member } from "@/lib/members";

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

const OPERATORS_BY_FIELD: Record<FilterFieldName, FilterOperatorName[]> = {
  status: ["Equals", "NotEquals", "IsEmpty", "IsNotEmpty"],
  assignee: ["Equals", "NotEquals", "IsEmpty", "IsNotEmpty"],
  tag: ["Equals", "NotEquals", "IsEmpty", "IsNotEmpty"],
  priority: ["Equals", "NotEquals", "In"],
  title: ["Contains", "Equals", "NotEquals", "IsEmpty", "IsNotEmpty"],
  duedate: ["Equals", "NotEquals", "GreaterThan", "LessThan", "IsEmpty", "IsNotEmpty"],
  startdate: ["Equals", "NotEquals", "GreaterThan", "LessThan", "IsEmpty", "IsNotEmpty"],
  iscompleted: ["Equals"],
};

const fieldClassName =
  "h-8 rounded-md border border-border bg-background px-2 text-xs font-normal text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

function needsValue(operator: FilterOperatorName) {
  return operator !== "IsEmpty" && operator !== "IsNotEmpty";
}

function ValueInput({
  condition,
  statuses,
  members,
  tags,
  onChange,
}: {
  condition: FilterCondition;
  statuses: StatusDefinition[];
  members: Member[];
  tags: Tag[];
  onChange: (value: string) => void;
}) {
  if (!needsValue(condition.operator)) {
    return null;
  }

  if (condition.field === "status") {
    return (
      <select className={fieldClassName} value={condition.value ?? ""} onChange={(e) => onChange(e.target.value)}>
        <option value="">Choose…</option>
        {statuses.map((status) => (
          <option key={status.id} value={status.id}>
            {status.name}
          </option>
        ))}
      </select>
    );
  }

  if (condition.field === "assignee") {
    return (
      <select className={fieldClassName} value={condition.value ?? ""} onChange={(e) => onChange(e.target.value)}>
        <option value="">Choose…</option>
        {members.map((member) => (
          <option key={member.userId} value={member.userId}>
            {member.displayName || member.email || member.userId}
          </option>
        ))}
      </select>
    );
  }

  if (condition.field === "tag") {
    return (
      <select className={fieldClassName} value={condition.value ?? ""} onChange={(e) => onChange(e.target.value)}>
        <option value="">Choose…</option>
        {tags.map((tag) => (
          <option key={tag.id} value={tag.id}>
            {tag.name}
          </option>
        ))}
      </select>
    );
  }

  if (condition.field === "priority") {
    return (
      <input
        className={fieldClassName}
        placeholder="Urgent,High"
        value={condition.value ?? ""}
        onChange={(e) => onChange(e.target.value)}
      />
    );
  }

  if (condition.field === "duedate" || condition.field === "startdate") {
    return (
      <input
        type="date"
        className={fieldClassName}
        value={condition.value ?? ""}
        onChange={(e) => onChange(e.target.value)}
      />
    );
  }

  if (condition.field === "iscompleted") {
    return (
      <select className={fieldClassName} value={condition.value ?? "true"} onChange={(e) => onChange(e.target.value)}>
        <option value="true">Yes</option>
        <option value="false">No</option>
      </select>
    );
  }

  return (
    <input className={fieldClassName} value={condition.value ?? ""} onChange={(e) => onChange(e.target.value)} />
  );
}

function ConditionRow({
  condition,
  statuses,
  members,
  tags,
  onChange,
  onRemove,
}: {
  condition: FilterCondition;
  statuses: StatusDefinition[];
  members: Member[];
  tags: Tag[];
  onChange: (next: FilterCondition) => void;
  onRemove: () => void;
}) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <select
        className={fieldClassName}
        value={condition.field}
        onChange={(e) => {
          const field = e.target.value as FilterFieldName;
          onChange({ field, operator: OPERATORS_BY_FIELD[field][0], value: undefined });
        }}
      >
        {FIELD_OPTIONS.map(([value, label]) => (
          <option key={value} value={value}>
            {label}
          </option>
        ))}
      </select>
      <select
        className={fieldClassName}
        value={condition.operator}
        onChange={(e) => onChange({ ...condition, operator: e.target.value as FilterOperatorName })}
      >
        {OPERATORS_BY_FIELD[condition.field].map((operator) => (
          <option key={operator} value={operator}>
            {operator}
          </option>
        ))}
      </select>
      <ValueInput
        condition={condition}
        statuses={statuses}
        members={members}
        tags={tags}
        onChange={(value) => onChange({ ...condition, value })}
      />
      <Button type="button" variant="ghost" size="sm" onClick={onRemove} aria-label="Remove condition">
        ✕
      </Button>
    </div>
  );
}

type GroupEditorProps = {
  group: FilterGroup;
  statuses: StatusDefinition[];
  members: Member[];
  tags: Tag[];
  onChange: (next: FilterGroup) => void;
  onRemove?: () => void;
  /**  . uc(t)he UI caps nesting at one level (root + one child group) to stay usable; the backend
   * (TaskFilterEvaluator/FilterGroupDto) itself supports arbitrary depth. */
  allowNestedGroups: boolean;
};

function GroupEditor({ group, statuses, members, tags, onChange, onRemove, allowNestedGroups }: GroupEditorProps) {
  const conditions = group.conditions ?? [];
  const groups = group.groups ?? [];

  function updateCondition(index: number, next: FilterCondition) {
    const nextConditions = [...conditions];
    nextConditions[index] = next;
    onChange({ ...group, conditions: nextConditions });
  }

  function removeCondition(index: number) {
    onChange({ ...group, conditions: conditions.filter((_, i) => i !== index) });
  }

  function addCondition() {
    onChange({ ...group, conditions: [...conditions, { field: "status", operator: "Equals", value: undefined }] });
  }

  function addGroup() {
    onChange({ ...group, groups: [...groups, { logic: "Or", conditions: [] }] });
  }

  return (
    <div className="space-y-3 rounded-lg border border-border bg-background p-3">
      <div className="flex items-center justify-between gap-2">
        <div className="inline-flex items-center gap-2 text-xs font-medium text-muted-foreground">
          Match
          <select
            className={fieldClassName}
            value={group.logic}
            onChange={(e) => onChange({ ...group, logic: e.target.value as "And" | "Or" })}
          >
            <option value="And">all</option>
            <option value="Or">any</option>
          </select>
          of:
        </div>
        {onRemove ? (
          <Button type="button" variant="ghost" size="sm" onClick={onRemove} aria-label="Remove group">
            ✕ group
          </Button>
        ) : null}
      </div>

      <div className="space-y-2">
        {conditions.map((condition, index) => (
          <ConditionRow
            key={index}
            condition={condition}
            statuses={statuses}
            members={members}
            tags={tags}
            onChange={(next) => updateCondition(index, next)}
            onRemove={() => removeCondition(index)}
          />
        ))}
      </div>

      {groups.map((childGroup, index) => (
        <GroupEditor
          key={index}
          group={childGroup}
          statuses={statuses}
          members={members}
          tags={tags}
          allowNestedGroups={false}
          onChange={(next) => {
            const nextGroups = [...groups];
            nextGroups[index] = next;
            onChange({ ...group, groups: nextGroups });
          }}
          onRemove={() => onChange({ ...group, groups: groups.filter((_, i) => i !== index) })}
        />
      ))}

      <div className="flex flex-wrap gap-2">
        <Button type="button" variant="outline" size="sm" onClick={addCondition}>
          + Condition
        </Button>
        {allowNestedGroups ? (
          <Button type="button" variant="outline" size="sm" onClick={addGroup}>
            + Nested group
          </Button>
        ) : null}
      </div>
    </div>
  );
}

export function FilterBuilder({
  group,
  statuses,
  members,
  tags,
  onChange,
}: {
  group: FilterGroup;
  statuses: StatusDefinition[];
  members: Member[];
  tags: Tag[];
  onChange: (next: FilterGroup) => void;
}) {
  return (
    <GroupEditor
      group={group}
      statuses={statuses}
      members={members}
      tags={tags}
      allowNestedGroups
      onChange={onChange}
    />
  );
}
