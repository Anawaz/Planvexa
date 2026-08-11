"use client";

import { type FormEvent, useState } from "react";
import { Button } from "@/components/ui/Button";
import { submitPublicComment } from "@/lib/collab/client";

/** Comment box shown on a public task page when the share link grants Comment (not View-only) access. */
export function PublicCommentForm({ token }: { token: string }) {
  const [guestName, setGuestName] = useState("");
  const [body, setBody] = useState("");
  const [status, setStatus] = useState<"idle" | "sending" | "sent" | "error">("idle");

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!body.trim()) {
      return;
    }

    setStatus("sending");
    try {
      await submitPublicComment(token, body.trim(), guestName.trim() || undefined);
      setBody("");
      setStatus("sent");
    } catch {
      setStatus("error");
    }
  }

  return (
    <section aria-labelledby="public-comment-title" className="space-y-3 border-t border-border pt-4">
      <h2 id="public-comment-title" className="text-sm font-semibold">
        Leave a comment
      </h2>
      <form className="space-y-2" onSubmit={handleSubmit}>
        <input
          value={guestName}
          onChange={(event) => setGuestName(event.currentTarget.value)}
          placeholder="Your name (optional)"
          className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        />
        <textarea
          value={body}
          onChange={(event) => setBody(event.currentTarget.value)}
          placeholder="Write a comment…"
          rows={3}
          className="w-full rounded-lg border border-border bg-background px-3 py-2 text-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        />
        <div className="flex items-center justify-between gap-3">
          {status === "sent" ? <p className="text-xs text-emerald-600 dark:text-emerald-400">Comment posted.</p> : null}
          {status === "error" ? <p className="text-xs text-red-600 dark:text-red-400">Could not post the comment.</p> : null}
          <Button type="submit" size="sm" disabled={status === "sending" || !body.trim()} className="ml-auto">
            {status === "sending" ? "Posting…" : "Post comment"}
          </Button>
        </div>
      </form>
    </section>
  );
}
