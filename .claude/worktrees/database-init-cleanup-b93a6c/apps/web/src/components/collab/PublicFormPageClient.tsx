"use client";

import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import { submitPublicForm, uploadPublicFormFile } from "@/lib/collab/client";
import type { PublicFormFieldDef } from "@/lib/collab/types";

type PublicFormPageClientProps = {
  token: string;
  fields: PublicFormFieldDef[];
  brandingColor?: string | null;
  confirmationMessage?: string | null;
  confirmationRedirectUrl?: string | null;
};

/**  . uc(m)irrors Form.IsFieldVisible on the backend — a field with no condition is always
 * visible; server-side validation independently re-derives this, so a client bypass can't smuggle a
 * hidden required field past submission (see PublicFormService.SubmitAsync). */
function isFieldVisible(field: PublicFormFieldDef, values: Record<string, string>): boolean {
  if (!field.conditionFieldId || !field.conditionOperator) return true;
  const actual = values[field.conditionFieldId] ?? "";
  const expected = field.conditionValue ?? "";
  switch (field.conditionOperator) {
    case "Equals":
      return actual.toLowerCase() === expected.toLowerCase();
    case "NotEquals":
      return actual.toLowerCase() !== expected.toLowerCase();
    case "Contains":
      return actual.toLowerCase().includes(expected.toLowerCase());
    case "IsEmpty":
      return actual.trim().length === 0;
    case "IsNotEmpty":
      return actual.trim().length > 0;
    default:
      return true;
  }
}

export function PublicFormPageClient({
  token,
  fields,
  brandingColor,
  confirmationMessage,
  confirmationRedirectUrl,
}: PublicFormPageClientProps) {
  const [values, setValues] = useState<Record<string, string>>({});
  const [honeypot, setHoneypot] = useState("");
  const [uploadingFieldId, setUploadingFieldId] = useState<string | null>(null);
  const [status, setStatus] = useState<"idle" | "submitting" | "submitted" | "error">("idle");
  const [message, setMessage] = useState("");
  // When the form was first rendered — sent back on submit so the server can reject
  // implausibly-fast bot submissions (Form.IsSpamSubmission).
  const renderedAtUtc = useRef(new Date().toISOString());
  const accentStyle = useMemo(
    () => (brandingColor ? ({ ["--form-accent" as string]: brandingColor } as React.CSSProperties) : undefined),
    [brandingColor],
  );

  useEffect(() => {
    if (status === "submitted" && confirmationRedirectUrl) {
      window.location.href = confirmationRedirectUrl;
    }
  }, [status, confirmationRedirectUrl]);

  const orderedFields = [...fields].sort((a, b) => a.position - b.position);
  const visibleFields = orderedFields.filter((field) => isFieldVisible(field, values));

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setStatus("submitting");
    setMessage("");

    try {
      // Only send values for currently-visible fields — matches the server's own re-derivation, and
      // keeps a stale hidden-field value from being submitted after the user changed their answer.
      const visibleIds = new Set(visibleFields.map((f) => f.id));
      const effectiveValues = Object.fromEntries(Object.entries(values).filter(([id]) => visibleIds.has(id)));

      await submitPublicForm(token, effectiveValues, honeypot, renderedAtUtc.current);
      setStatus("submitted");
      setMessage(confirmationMessage || "Thanks. Your response was received.");
      setValues({});
    } catch (error) {
      setStatus("error");
      setMessage(error instanceof Error ? error.message : "Unable to submit this form.");
    }
  }

  function updateValue(fieldId: string, value: string) {
    setValues((current) => ({ ...current, [fieldId]: value }));
  }

  async function handleFileChange(fieldId: string, file: File | null) {
    if (!file) {
      updateValue(fieldId, "");
      return;
    }

    setUploadingFieldId(fieldId);
    try {
      const result = await uploadPublicFormFile(token, file);
      updateValue(fieldId, result.uploadId);
    } catch (error) {
      setStatus("error");
      setMessage(error instanceof Error ? error.message : "Unable to upload this file.");
    } finally {
      setUploadingFieldId(null);
    }
  }

  return (
    <form className="space-y-5" style={accentStyle} onSubmit={handleSubmit}>
      {visibleFields.map((field) => {
        const id = `public-form-${field.id}`;
        const value = values[field.id] ?? "";

        return (
          <label key={field.id} htmlFor={id} className="grid gap-2 text-sm font-medium">
            <span>
              {field.label}
              {field.required ? <span className="text-red-600 dark:text-red-400"> *</span> : null}
            </span>
            {field.type === "LongText" ? (
              <textarea
                id={id}
                value={value}
                required={field.required}
                rows={5}
                className="rounded-lg border border-border bg-background px-3 py-2 text-sm font-normal outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                onChange={(event) => updateValue(field.id, event.target.value)}
              />
            ) : field.type === "Select" ? (
              <select
                id={id}
                value={value}
                required={field.required}
                className="h-11 rounded-lg border border-border bg-background px-3 text-sm font-normal outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                onChange={(event) => updateValue(field.id, event.target.value)}
              >
                <option value="">Choose an option</option>
                {field.options.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            ) : field.type === "Boolean" ? (
              <span className="flex items-center gap-2">
                <input
                  id={id}
                  type="checkbox"
                  checked={value === "true"}
                  onChange={(event) => updateValue(field.id, event.target.checked ? "true" : "false")}
                  className="size-4 rounded border-border accent-[var(--form-accent,var(--primary))]"
                />
              </span>
            ) : field.type === "FileUpload" ? (
              <span className="grid gap-1">
                <input
                  id={id}
                  type="file"
                  required={field.required && !value}
                  onChange={(event) => void handleFileChange(field.id, event.target.files?.[0] ?? null)}
                  className="text-sm"
                />
                {uploadingFieldId === field.id ? (
                  <span className="text-xs text-muted-foreground">Uploading…</span>
                ) : value ? (
                  <span className="text-xs text-emerald-600 dark:text-emerald-400">File attached.</span>
                ) : null}
              </span>
            ) : (
              <input
                id={id}
                type={
                  field.type === "Number"
                    ? "number"
                    : field.type === "Date"
                      ? "date"
                      : field.type === "Email"
                        ? "email"
                        : field.type === "Phone"
                          ? "tel"
                          : field.type === "Url"
                            ? "url"
                            : "text"
                }
                value={value}
                required={field.required}
                className="h-11 rounded-lg border border-border bg-background px-3 text-sm font-normal outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                onChange={(event) => updateValue(field.id, event.target.value)}
              />
            )}
          </label>
        );
      })}

      {/* Invisible honeypot — a real visitor never sees or fills this field; a bot
          that blindly fills every input does, and the server rejects the submission (Form.IsSpamSubmission).
          aria-hidden + tabIndex=-1 + off-screen positioning (not display:none, which some bots skip). */}
      <div aria-hidden="true" className="absolute -left-[9999px] top-auto h-px w-px overflow-hidden">
        <label htmlFor="public-form-website">Leave this field empty</label>
        <input
          id="public-form-website"
          type="text"
          tabIndex={-1}
          autoComplete="off"
          value={honeypot}
          onChange={(event) => setHoneypot(event.target.value)}
        />
      </div>

      {message ? (
        <p
          role="status"
          className={
            status === "error"
              ? "rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
              : "rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm text-emerald-700 dark:border-emerald-900 dark:bg-emerald-950 dark:text-emerald-300"
          }
        >
          {message}
        </p>
      ) : null}

      <Button
        type="submit"
        disabled={status === "submitting" || uploadingFieldId !== null}
        className="w-full sm:w-auto"
        style={brandingColor ? { backgroundColor: brandingColor, borderColor: brandingColor } : undefined}
      >
        {status === "submitting" ? "Submitting..." : "Submit response"}
      </Button>
    </form>
  );
}
