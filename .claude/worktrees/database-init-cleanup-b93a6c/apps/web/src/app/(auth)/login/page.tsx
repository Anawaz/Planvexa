import Link from "next/link";


export default function LoginPage() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12">
      <section className="w-full max-w-md rounded-[var(--radius)] border border-border bg-card p-8 shadow-xl shadow-slate-950/10 dark:shadow-black/30">
        <Link href="/" className="text-sm font-medium text-muted-foreground hover:text-foreground">← Back to home</Link>
        <div className="mt-8 space-y-3">
          <p className="text-sm font-medium text-primary">Authentication</p>
          <h1 className="text-3xl font-semibold tracking-tight">Log in to Planvexa</h1>
          <p className="text-sm leading-6 text-muted-foreground">Sign in with the local Keycloak realm. Tokens are handled by the server and stored in an encrypted HttpOnly session cookie.</p>
        </div>
        <div className="mt-8 space-y-4">
          {/* Plain anchor, not Link: /auth/login is a GET route that 307s to Keycloak, and the
              router's RSC fetch of a cross-origin redirect is blocked by CORS. */}
          <a href="/auth/login" aria-describedby="sso-help" className="inline-flex h-11 w-full items-center justify-center rounded-lg bg-primary px-4 text-sm font-medium text-primary-foreground transition-[transform,opacity] active:scale-[0.97] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
            Continue with Keycloak
          </a>
          <p id="sso-help" className="text-center text-xs text-muted-foreground">
            Development users: owner@planvexa.local, admin@planvexa.local, member@planvexa.local, guest@planvexa.local.
          </p>
          <p className="text-center text-xs text-muted-foreground">
            <Link href="/legal" className="underline underline-offset-4 hover:text-foreground">
              Legal, licence, and source code information
            </Link>
          </p>
        </div>
      </section>
    </main>
  );
}

