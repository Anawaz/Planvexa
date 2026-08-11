import { Suspense } from "react";
import { ChatPageClient } from "@/components/chat/ChatPageClient";

export default function ChatPage() {
  return (
    <Suspense
      fallback={
        <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading chat…
        </section>
      }
    >
      <ChatPageClient />
    </Suspense>
  );
}
