"use client";

import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import type { CSSProperties, FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { MemberSelect } from "@/components/people/MemberSelect";
import { TeamSelect } from "@/components/people/TeamSelect";
import {
  createForm,
  deleteForm,
  exportFormSubmissionsCsvHref,
  exportFormSubmissionsXlsxHref,
  getForm,
  getFormSubmissions,
  listForms,
  updateForm,
  updateFormSettings,
} from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import type { Form as CollabForm, FormFieldConditionOperator, FormFieldDef, UpdateFormSettingsInput } from "@/lib/collab/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { listEffectiveCustomFields } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import { useWorkspaceLists } from "@/lib/work/useWorkspaceLists";
import { cn } from "@/lib/utils";
import {
  copyToClipboard,
  formatIsoDateTime,
  numberFormatter,
  panelClassName,
  textInputClassName,
  textareaClassName,
} from "./collab-ui";

const fieldTypes: FormFieldDef["type"][] = [
  "Text",
  "LongText",
  "Number",
  "Date",
  "Select",
  "Boolean",
  "Email",
  "Phone",
  "Url",
  "FileUpload",
];

const conditionOperators: { value: FormFieldConditionOperator; label: string }[] = [
  { value: "Equals", label: "equals" },
  { value: "NotEquals", label: "does not equal" },
  { value: "Contains", label: "contains" },
  { value: "IsEmpty", label: "is empty" },
  { value: "IsNotEmpty", label: "is not empty" },
];

// Formula/Rollup are computed, Relationship needs the dedicated relationships endpoint, and User needs a
// workspace-membership check the anonymous submission path can't do — mirrors TaskWriteApi's
// SetCustomFieldValueAsync scope decision (see that method's doc comment on the backend).
const unmappableCustomFieldTypes = new Set(["Formula", "Rollup", "Relationship", "User"]);

type FormDraft = Pick<CollabForm, "title" | "isActive" | "fields"> & {
  description: string;
};

function draftFromForm(form: CollabForm): FormDraft {
  return {
    title: form.title,
    description: form.description ?? "",
    isActive: form.isActive,
    fields: [...form.fields].sort((left, right) => left.position - right.position),
  };
}

function renumberFields(fields: FormFieldDef[]) {
  return fields.map((field, index) => ({ ...field, position: index + 1 }));
}

/** Drag-and-drop reorder: move the field with `activeId` to where `overId` was dropped. */
export function reorderFields(fields: FormFieldDef[], activeId: string, overId: string): FormFieldDef[] {
  const oldIndex = fields.findIndex((field) => field.id === activeId);
  const newIndex = fields.findIndex((field) => field.id === overId);
  if (oldIndex < 0 || newIndex < 0 || oldIndex === newIndex) {
    return fields;
  }

  return renumberFields(arrayMove(fields, oldIndex, newIndex));
}

function newField(position: number): FormFieldDef {
  return {
    id: `field-${Date.now().toString(36)}-${position}`,
    label: `Question ${position}`,
    type: "Text",
    required: false,
    options: [],
    position,
  };
}

type SettingsDraft = {
  brandingLogoUrl: string;
  brandingColor: string;
  confirmationMessage: string;
  confirmationRedirectUrl: string;
  minSubmitSeconds: string;
  maxTotalSubmissions: string;
  maxSubmissionsPerRespondent: string;
  targetStatusName: string;
  targetPriority: string;
  targetTagsCsv: string;
  targetTeamId: string;
  targetUserId: string;
  dueDateDaysAfterSubmission: string;
};

function settingsDraftFromForm(form: CollabForm): SettingsDraft {
  return {
    brandingLogoUrl: form.brandingLogoUrl ?? "",
    brandingColor: form.brandingColor ?? "",
    confirmationMessage: form.confirmationMessage ?? "",
    confirmationRedirectUrl: form.confirmationRedirectUrl ?? "",
    minSubmitSeconds: form.minSubmitSeconds?.toString() ?? "",
    maxTotalSubmissions: form.maxTotalSubmissions?.toString() ?? "",
    maxSubmissionsPerRespondent: form.maxSubmissionsPerRespondent?.toString() ?? "",
    targetStatusName: form.targetStatusName ?? "",
    targetPriority: form.targetPriority ?? "",
    targetTagsCsv: form.targetTags.join(", "),
    targetTeamId: form.targetTeamId ?? "",
    targetUserId: form.targetUserId ?? "",
    dueDateDaysAfterSubmission: form.dueDateDaysAfterSubmission?.toString() ?? "",
  };
}

function toNullableInt(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const parsed = Number.parseInt(trimmed, 10);
  return Number.isFinite(parsed) ? parsed : null;
}

function toSettingsInput(draft: SettingsDraft): UpdateFormSettingsInput {
  return {
    brandingLogoUrl: draft.brandingLogoUrl.trim() || null,
    brandingColor: draft.brandingColor.trim() || null,
    confirmationMessage: draft.confirmationMessage.trim() || null,
    confirmationRedirectUrl: draft.confirmationRedirectUrl.trim() || null,
    minSubmitSeconds: toNullableInt(draft.minSubmitSeconds),
    maxTotalSubmissions: toNullableInt(draft.maxTotalSubmissions),
    maxSubmissionsPerRespondent: toNullableInt(draft.maxSubmissionsPerRespondent),
    targetStatusName: draft.targetStatusName.trim() || null,
    targetPriority: draft.targetPriority.trim() || null,
    targetTagsCsv: draft.targetTagsCsv.trim() || null,
    targetTeamId: draft.targetTeamId.trim() || null,
    targetUserId: draft.targetUserId.trim() || null,
    dueDateDaysAfterSubmission: toNullableInt(draft.dueDateDaysAfterSubmission),
  };
}

export function FormSettingsPanel({ form }: { form: CollabForm }) {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [draft, setDraft] = useState<SettingsDraft>(() => settingsDraftFromForm(form));
  const saveMutation = useMutation({
    mutationFn: () => updateFormSettings(form.id, toSettingsInput(draft)),
    onSuccess: (savedForm) => {
      setDraft(settingsDraftFromForm(savedForm));
      void queryClient.invalidateQueries({ queryKey: collabKeys.formsRoot(workspaceId) });
    },
  });

  function patch(next: Partial<SettingsDraft>) {
    setDraft((current) => ({ ...current, ...next }));
  }

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    saveMutation.mutate();
  }

  return (
    <form onSubmit={save} className={cn(panelClassName, "p-4")}>
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-sm font-semibold">Branding, limits &amp; routing</h3>
        <Button type="submit" size="sm" disabled={saveMutation.isPending}>
          Save settings
        </Button>
      </div>

      <fieldset className="mt-4 grid gap-3 lg:grid-cols-2">
        <legend className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Branding</legend>
        <label className="grid gap-1 text-xs font-medium">
          Logo URL
          <input value={draft.brandingLogoUrl} onChange={(e) => patch({ brandingLogoUrl: e.target.value })} className={textInputClassName} placeholder="https://…" />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Primary color
          <input value={draft.brandingColor} onChange={(e) => patch({ brandingColor: e.target.value })} className={textInputClassName} placeholder="#4f46e5" />
        </label>
      </fieldset>

      <fieldset className="mt-4 grid gap-3 lg:grid-cols-2">
        <legend className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Confirmation page</legend>
        <label className="grid gap-1 text-xs font-medium">
          Success message
          <input value={draft.confirmationMessage} onChange={(e) => patch({ confirmationMessage: e.target.value })} className={textInputClassName} placeholder="Thanks — we'll be in touch." />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Redirect URL (optional)
          <input value={draft.confirmationRedirectUrl} onChange={(e) => patch({ confirmationRedirectUrl: e.target.value })} className={textInputClassName} placeholder="https://…" />
        </label>
      </fieldset>

      <fieldset className="mt-4 grid gap-3 lg:grid-cols-3">
        <legend className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          Spam &amp; submission limits
        </legend>
        <label className="grid gap-1 text-xs font-medium">
          Min. seconds before submit
          <input type="number" min={0} value={draft.minSubmitSeconds} onChange={(e) => patch({ minSubmitSeconds: e.target.value })} className={textInputClassName} placeholder="2 (default)" />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Max total submissions
          <input type="number" min={0} value={draft.maxTotalSubmissions} onChange={(e) => patch({ maxTotalSubmissions: e.target.value })} className={textInputClassName} placeholder="Unlimited" />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Max per respondent
          <input type="number" min={0} value={draft.maxSubmissionsPerRespondent} onChange={(e) => patch({ maxSubmissionsPerRespondent: e.target.value })} className={textInputClassName} placeholder="Unlimited" />
        </label>
      </fieldset>

      <fieldset className="mt-4 grid gap-3 lg:grid-cols-3">
        <legend className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          Task routing (on top of the target list)
        </legend>
        <label className="grid gap-1 text-xs font-medium">
          Status name
          <input value={draft.targetStatusName} onChange={(e) => patch({ targetStatusName: e.target.value })} className={textInputClassName} placeholder="e.g. To Do" />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Priority
          <select value={draft.targetPriority} onChange={(e) => patch({ targetPriority: e.target.value })} className={textInputClassName}>
            <option value="">Unset</option>
            <option value="Low">Low</option>
            <option value="Normal">Normal</option>
            <option value="High">High</option>
            <option value="Urgent">Urgent</option>
          </select>
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Tags, comma separated
          <input value={draft.targetTagsCsv} onChange={(e) => patch({ targetTagsCsv: e.target.value })} className={textInputClassName} placeholder="vip, intake" />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Team
          <TeamSelect
            value={draft.targetTeamId}
            onChange={(teamId) => patch({ targetTeamId: teamId })}
            includeAny
            anyLabel="No team"
            className="h-9 w-full"
          />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Assign to member
          <MemberSelect
            value={draft.targetUserId}
            onChange={(userId) => patch({ targetUserId: userId })}
            includeAny
            anyLabel="Unassigned"
            className="h-9 w-full"
          />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Due date, days after submission
          <input type="number" min={0} value={draft.dueDateDaysAfterSubmission} onChange={(e) => patch({ dueDateDaysAfterSubmission: e.target.value })} className={textInputClassName} placeholder="e.g. 3" />
        </label>
      </fieldset>
    </form>
  );
}

type FormFieldRowProps = {
  field: FormFieldDef;
  index: number;
  fieldsLength: number;
  otherFields: FormFieldDef[];
  mappableCustomFields: { id: string; name: string; type: string }[];
  updateField: (id: string, patch: Partial<FormFieldDef>) => void;
  moveField: (id: string, direction: -1 | 1) => void;
  removeField: (id: string) => void;
};

/** One draggable/sortable field row in the form builder — order also adjustable via Up/Down. */
function FormFieldRow({
  field,
  index,
  fieldsLength,
  otherFields,
  mappableCustomFields,
  updateField,
  moveField,
  removeField,
}: FormFieldRowProps) {
  const { attributes, isDragging, listeners, setActivatorNodeRef, setNodeRef, transform, transition } = useSortable({
    id: field.id,
  });
  const style: CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <article
      ref={setNodeRef}
      style={style}
      className={cn(
        "rounded-xl border border-border bg-background p-3",
        isDragging && "opacity-60 shadow-lg",
      )}
    >
      <div className="grid gap-3 lg:grid-cols-[1.5fr_10rem_auto]">
        <label className="grid gap-1 text-xs font-medium">
          Label
          <input
            value={field.label}
            onChange={(event) => updateField(field.id, { label: event.target.value })}
            className={textInputClassName}
          />
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Type
          <select
            value={field.type}
            onChange={(event) =>
              updateField(field.id, {
                type: event.target.value as FormFieldDef["type"],
                options: event.target.value === "Select" ? field.options : [],
              })
            }
            className={textInputClassName}
          >
            {fieldTypes.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </select>
        </label>
        <div className="flex flex-wrap items-end gap-2">
          <button
            ref={setActivatorNodeRef}
            type="button"
            className="rounded px-1 text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            aria-label={`Drag ${field.label}`}
            {...attributes}
            {...listeners}
          >
            ⋮⋮
          </button>
          <Button type="button" variant="outline" size="sm" onClick={() => moveField(field.id, -1)} disabled={index === 0}>
            Up
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={() => moveField(field.id, 1)} disabled={index === fieldsLength - 1}>
            Down
          </Button>
          <Button type="button" variant="ghost" size="sm" onClick={() => removeField(field.id)}>
            Remove
          </Button>
        </div>
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-[auto_1fr]">
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={field.required}
            onChange={(event) => updateField(field.id, { required: event.target.checked })}
            className="size-4 rounded border-border accent-[var(--primary)]"
          />
          Required
        </label>
        {field.type === "Select" ? (
          <label className="grid gap-1 text-xs font-medium">
            Options, comma separated
            <input
              value={field.options.join(", ")}
              onChange={(event) =>
                updateField(field.id, {
                  options: event.target.value.split(",").map((option) => option.trim()),
                })
              }
              className={textInputClassName}
            />
          </label>
        ) : null}
      </div>

      {/* Conditional visibility — "show this field only if <other field> <op> <value>". */}
      <div className="mt-3 grid gap-3 rounded-lg border border-dashed border-border p-3 lg:grid-cols-[1fr_10rem_1fr]">
        <label className="grid gap-1 text-xs font-medium">
          Show only if
          <select
            value={field.conditionFieldId ?? ""}
            onChange={(event) =>
              updateField(field.id, {
                conditionFieldId: event.target.value || null,
                conditionOperator: event.target.value ? (field.conditionOperator ?? "Equals") : null,
              })
            }
            className={textInputClassName}
          >
            <option value="">Always visible</option>
            {otherFields.map((other) => (
              <option key={other.id} value={other.id}>
                {other.label}
              </option>
            ))}
          </select>
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Operator
          <select
            value={field.conditionOperator ?? "Equals"}
            onChange={(event) => updateField(field.id, { conditionOperator: event.target.value as FormFieldConditionOperator })}
            className={textInputClassName}
            disabled={!field.conditionFieldId}
          >
            {conditionOperators.map((op) => (
              <option key={op.value} value={op.value}>
                {op.label}
              </option>
            ))}
          </select>
        </label>
        <label className="grid gap-1 text-xs font-medium">
          Value
          <input
            value={field.conditionValue ?? ""}
            onChange={(event) => updateField(field.id, { conditionValue: event.target.value })}
            className={textInputClassName}
            disabled={!field.conditionFieldId || field.conditionOperator === "IsEmpty" || field.conditionOperator === "IsNotEmpty"}
          />
        </label>
      </div>

      {/* Map this field onto a WorkManagement custom field on the target list. */}
      <label className="mt-3 grid gap-1 text-xs font-medium">
        Map to task custom field
        <select
          value={field.customFieldDefinitionId ?? ""}
          onChange={(event) => updateField(field.id, { customFieldDefinitionId: event.target.value || null })}
          className={textInputClassName}
        >
          <option value="">Not mapped (built-in fields only)</option>
          {mappableCustomFields.map((cf) => (
            <option key={cf.id} value={cf.id}>
              {cf.name} ({cf.type})
            </option>
          ))}
        </select>
      </label>
    </article>
  );
}

function FormBuilder({ form, onDeleted }: { form: CollabForm; onDeleted: () => void }) {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [draft, setDraft] = useState<FormDraft>(() => draftFromForm(form));
  const [copyStatus, setCopyStatus] = useState("");
  const customFieldsQuery = useQuery({
    queryKey: [...workKeys.lists(form.listId), "custom-fields"],
    queryFn: () => listEffectiveCustomFields(form.listId),
  });
  const mappableCustomFields = (customFieldsQuery.data ?? []).filter((f) => !unmappableCustomFieldTypes.has(f.type));

  const saveMutation = useMutation({
    mutationFn: ({ id, value }: { id: string; value: FormDraft }) =>
      updateForm(id, {
        title: value.title,
        description: value.description,
        isActive: value.isActive,
        fields: value.fields,
      }),
    onSuccess: (savedForm) => {
      setDraft(draftFromForm(savedForm));
      void queryClient.invalidateQueries({ queryKey: collabKeys.formsRoot(workspaceId) });
    },
  });
  const deleteMutation = useMutation({
    mutationFn: deleteForm,
    onSuccess: () => {
      onDeleted();
      void queryClient.invalidateQueries({ queryKey: collabKeys.formsRoot(workspaceId) });
    },
  });
  const fieldSensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 8 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  function handleFieldDragEnd(event: DragEndEvent) {
    const overId = event.over?.id;
    if (!overId || event.active.id === overId) {
      return;
    }

    setDraft((current) => ({
      ...current,
      fields: reorderFields(current.fields, String(event.active.id), String(overId)),
    }));
  }

  function saveDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    saveMutation.mutate({ id: form.id, value: { ...draft, fields: renumberFields(draft.fields) } });
  }

  function updateField(id: string, patch: Partial<FormFieldDef>) {
    setDraft((current) => ({
      ...current,
      fields: current.fields.map((field) => (field.id === id ? { ...field, ...patch } : field)),
    }));
  }

  function moveField(id: string, direction: -1 | 1) {
    setDraft((current) => {
      const index = current.fields.findIndex((field) => field.id === id);
      const nextIndex = index + direction;
      if (index < 0 || nextIndex < 0 || nextIndex >= current.fields.length) {
        return current;
      }

      const fields = [...current.fields];
      const [field] = fields.splice(index, 1);
      fields.splice(nextIndex, 0, field);
      return { ...current, fields: renumberFields(fields) };
    });
  }

  function removeField(id: string) {
    setDraft((current) => ({
      ...current,
      fields: renumberFields(current.fields.filter((field) => field.id !== id)),
    }));
  }

  function addField() {
    setDraft((current) => ({
      ...current,
      fields: [...current.fields, newField(current.fields.length + 1)],
    }));
  }

  function copyPublicUrl() {
    void copyToClipboard(`/public/forms/${form.publicToken}`).then(() => setCopyStatus("Copied public URL."));
  }

  return (
    <form onSubmit={saveDraft} className={cn(panelClassName, "p-4")}>
      <div className="flex flex-col gap-3 border-b border-border pb-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <h2 className="text-lg font-semibold">Form builder</h2>
          <p className="mt-1 text-xs text-muted-foreground">
            Public URL: <code>/public/forms/{form.publicToken}</code>
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" size="sm" onClick={copyPublicUrl}>
            Copy public URL
          </Button>
          <Button type="submit" size="sm" disabled={saveMutation.isPending || draft.fields.length === 0}>
            Save form
          </Button>
        </div>
      </div>

      {copyStatus ? (
        <p role="status" className="mt-3 rounded-lg bg-primary/10 px-3 py-2 text-xs text-primary">
          {copyStatus}
        </p>
      ) : null}

      <div className="mt-4 grid gap-4 lg:grid-cols-2">
        <label className="grid gap-1 text-xs font-medium">
          Title
          <input
            value={draft.title}
            onChange={(event) => setDraft({ ...draft, title: event.target.value })}
            className={textInputClassName}
          />
        </label>
        <label className="grid gap-1 text-xs font-medium lg:col-span-2">
          Description
          <textarea
            value={draft.description}
            onChange={(event) => setDraft({ ...draft, description: event.target.value })}
            className={textareaClassName}
          />
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={draft.isActive}
            onChange={(event) => setDraft({ ...draft, isActive: event.target.checked })}
            className="size-4 rounded border-border accent-[var(--primary)]"
          />
          Accept new public submissions
        </label>
      </div>

      <section className="mt-6" aria-labelledby="form-fields-title">
        <div className="flex items-center justify-between gap-3">
          <h3 id="form-fields-title" className="text-sm font-semibold">
            Fields
          </h3>
          <Button type="button" variant="secondary" size="sm" onClick={addField}>
            Add field
          </Button>
        </div>
        <DndContext sensors={fieldSensors} onDragEnd={handleFieldDragEnd}>
          <SortableContext items={draft.fields.map((field) => field.id)} strategy={verticalListSortingStrategy}>
            <div className="mt-3 space-y-3">
              {draft.fields.map((field, index) => (
                <FormFieldRow
                  key={field.id}
                  field={field}
                  index={index}
                  fieldsLength={draft.fields.length}
                  otherFields={draft.fields.filter((f) => f.id !== field.id)}
                  mappableCustomFields={mappableCustomFields}
                  updateField={updateField}
                  moveField={moveField}
                  removeField={removeField}
                />
              ))}
            </div>
          </SortableContext>
        </DndContext>
      </section>

      <div className="mt-4 flex justify-end border-t border-border pt-4">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={deleteMutation.isPending}
          onClick={() => deleteMutation.mutate(form.id)}
        >
          Delete form
        </Button>
      </div>
    </form>
  );
}

export function FormsPageClient() {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [selectedFormId, setSelectedFormId] = useState<string | null>(null);
  const [newFormTitle, setNewFormTitle] = useState("");
  const [newFormListId, setNewFormListId] = useState("");
  const listOptions = useWorkspaceLists();
  const formsQuery = useQuery({ queryKey: collabKeys.forms(workspaceId), queryFn: listForms });
  const forms = formsQuery.data ?? [];
  const activeFormId = selectedFormId ?? forms[0]?.id ?? "";
  const formQuery = useQuery({
    queryKey: collabKeys.form(workspaceId, activeFormId),
    queryFn: () => getForm(activeFormId),
    enabled: Boolean(activeFormId),
  });
  const submissionsQuery = useQuery({
    queryKey: collabKeys.formSubmissions(workspaceId, activeFormId),
    queryFn: () => getFormSubmissions(activeFormId),
    enabled: Boolean(activeFormId),
  });
  const createMutation = useMutation({
    mutationFn: createForm,
    onSuccess: (form) => {
      setNewFormTitle("");
      setSelectedFormId(form.id);
      void queryClient.invalidateQueries({ queryKey: collabKeys.formsRoot(workspaceId) });
    },
  });
  const targetListId = newFormListId || listOptions[0]?.id || "";

  function submitNewForm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!targetListId) {
      return;
    }

    createMutation.mutate({
      listId: targetListId,
      title: newFormTitle.trim() || "Untitled intake form",
      description: "Describe what requesters should submit.",
      fields: [newField(1)],
    });
  }

  const activeForm = formQuery.data;
  const submissions = submissionsQuery.data ?? [];

  return (
    <section aria-labelledby="forms-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Forms</p>
        <h1 id="forms-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Forms
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Build public intake forms; every submission creates a task in the target list, optionally routed
          to a status/priority/tags/team/due date and mapped onto task custom fields.
        </p>
      </div>

      <div className="grid gap-6 xl:grid-cols-[22rem_1fr]">
        <aside className="space-y-4">
          <section className={cn(panelClassName, "p-4")} aria-labelledby="forms-list-title">
            <h2 id="forms-list-title" className="text-sm font-semibold">
              Forms list
            </h2>
            <div className="mt-3 space-y-2">
              {formsQuery.isLoading ? (
                <p className="text-sm text-muted-foreground">Loading forms…</p>
              ) : forms.length === 0 ? (
                <EmptyState
                  title="No forms yet"
                  description="Build one with the form designer on this page to collect requests straight into a list."
                />
              ) : (
                forms.map((form) => (
                  <button
                    key={form.id}
                    type="button"
                    aria-pressed={activeFormId === form.id}
                    onClick={() => setSelectedFormId(form.id)}
                    className={cn(
                      "w-full rounded-xl border p-3 text-left transition focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none",
                      activeFormId === form.id
                        ? "border-primary bg-primary/10"
                        : "border-border bg-background hover:bg-muted",
                    )}
                  >
                    <span className="block text-sm font-semibold">{form.title}</span>
                    <span className="mt-1 block text-xs text-muted-foreground">
                      {numberFormatter.format(form.fields.length)} fields · {form.isActive ? "Active" : "Paused"}
                    </span>
                  </button>
                ))
              )}
            </div>
          </section>

          <form onSubmit={submitNewForm} className={cn(panelClassName, "p-4")}>
            <h2 className="text-sm font-semibold">Create form</h2>
            <div className="mt-4 grid gap-3">
              <label className="grid gap-1 text-xs font-medium">
                Title
                <input
                  value={newFormTitle}
                  onChange={(event) => setNewFormTitle(event.target.value)}
                  className={textInputClassName}
                  placeholder="Customer feedback"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium">
                Target list
                <select
                  value={targetListId}
                  onChange={(event) => setNewFormListId(event.target.value)}
                  className={textInputClassName}
                >
                  {listOptions.map((list) => (
                    <option key={list.id} value={list.id}>
                      {list.label}
                    </option>
                  ))}
                </select>
              </label>
              <Button type="submit" size="sm" disabled={createMutation.isPending || !targetListId}>
                Add form
              </Button>
            </div>
          </form>
        </aside>

        <div className="space-y-6">
          {activeForm ? (
            <>
              <FormBuilder key={activeForm.id} form={activeForm} onDeleted={() => setSelectedFormId(null)} />
              <FormSettingsPanel key={`${activeForm.id}-settings`} form={activeForm} />
            </>
          ) : (
            <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>
              Select or create a form to edit fields.
            </section>
          )}

          <section className={cn(panelClassName, "overflow-hidden")} aria-labelledby="submissions-title">
            <header className="flex flex-wrap items-center justify-between gap-3 border-b border-border p-4">
              <h2 id="submissions-title" className="text-sm font-semibold">
                Submissions ({numberFormatter.format(submissions.length)})
              </h2>
              {activeForm ? (
                <div className="flex gap-2">
                  <a
                    href={exportFormSubmissionsCsvHref(activeForm.id)}
                    className="rounded-lg border border-border bg-background px-3 py-1.5 text-xs font-medium hover:bg-muted"
                  >
                    Export CSV
                  </a>
                  <a
                    href={exportFormSubmissionsXlsxHref(activeForm.id)}
                    className="rounded-lg border border-border bg-background px-3 py-1.5 text-xs font-medium hover:bg-muted"
                  >
                    Export Excel
                  </a>
                </div>
              ) : null}
            </header>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[40rem] text-left text-sm">
                <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
                  <tr>
                    <th className="px-4 py-3 font-semibold">Submitted</th>
                    <th className="px-4 py-3 font-semibold">Created task</th>
                    {(activeForm?.fields ?? []).map((field) => (
                      <th key={field.id} className="px-4 py-3 font-semibold">
                        {field.label}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {submissions.map((submission) => (
                    <tr key={submission.id}>
                      <td className="px-4 py-3 text-muted-foreground">
                        {formatIsoDateTime(submission.submittedAtUtc)}
                      </td>
                      <td className="px-4 py-3">{submission.createdTaskId ?? "Not created"}</td>
                      {(activeForm?.fields ?? []).map((field) => (
                        <td key={field.id} className="px-4 py-3">
                          {submission.values[field.id] ?? "—"}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
              {submissionsQuery.isLoading ? (
                <p className="p-4 text-sm text-muted-foreground">Loading submissions…</p>
              ) : null}
            </div>
          </section>
        </div>
      </div>
    </section>
  );
}
