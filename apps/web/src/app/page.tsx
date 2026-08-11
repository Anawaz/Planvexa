import Link from "next/link";
import { buttonStyles } from "@/components/ui/Button";

const API_BASE_URL = (process.env.API_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080").replace(/\/$/, "");

// Anonymous, server-side: whether to offer self-service signup at all (Registration:
// AllowSelfRegistration — see UserDirectory.GetOrProvisionAsync's gate). Defaults to showing the
// buttons if the API can't be reached, rather than hiding a real signup path on a transient outage.
async function getAllowSelfRegistration(): Promise<boolean> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/v1/public/registration-policy`, { cache: "no-store" });
    if (!response.ok) return true;
    const data = (await response.json()) as { allowSelfRegistration: boolean };
    return data.allowSelfRegistration;
  } catch {
    return true;
  }
}

export default async function LandingPage() {
  const allowSelfRegistration = await getAllowSelfRegistration();

  return (
    <main className="min-h-screen overflow-hidden bg-background">
      <section className="mx-auto flex min-h-screen w-full max-w-6xl flex-col px-6 py-8 sm:px-8 lg:px-10">
        <nav className="flex items-center justify-between" aria-label="Landing">
          <Link href="/" className="text-lg font-semibold tracking-tight">
            Planvexa
          </Link>
          <div className="flex items-center gap-3">
            <Link
              className="text-sm font-medium text-muted-foreground hover:text-foreground"
              href="/legal"
            >
              Legal
            </Link>
            {/* Plain anchors, not Link: /auth/login and /auth/register are GET routes that 307 to
                Keycloak, and the router's RSC fetch of a cross-origin redirect is blocked by CORS. */}
            <a
              className="text-sm font-medium text-muted-foreground hover:text-foreground"
              href="/auth/login"
            >
              Log in
            </a>
            {allowSelfRegistration ? (
              <a
                className={buttonStyles({ variant: "secondary", size: "sm" })}
                href="/auth/register"
              >
                Sign up
              </a>
            ) : null}
          </div>
        </nav>

        <div className="grid flex-1 items-center gap-12 py-20 lg:grid-cols-[1.05fr_0.95fr]">
          <div className="max-w-3xl">
            <p className="mb-4 inline-flex rounded-full border border-border bg-card px-3 py-1 text-sm font-medium text-muted-foreground shadow-sm">
              Workspace platform
            </p>
            <h1 className="text-4xl font-semibold tracking-tight text-foreground sm:text-6xl">
              A calm foundation for workspace-based work management.
            </h1>
            <p className="mt-6 max-w-2xl text-lg leading-8 text-muted-foreground">
              Spaces, lists and tasks; docs, chat and whiteboards; time tracking,
              planning and reporting — one workspace, with the permissions and
              audit trail to run it for real.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row">
              {allowSelfRegistration ? (
                <Link className={buttonStyles({ size: "lg" })} href="/onboarding">
                  Start onboarding
                </Link>
              ) : null}
              <a
                className={buttonStyles({ variant: allowSelfRegistration ? "outline" : "primary", size: "lg" })}
                href="/auth/login"
              >
                Continue to login
              </a>
            </div>
          </div>

          <div className="rounded-[var(--radius)] border border-border bg-card p-6 shadow-2xl shadow-slate-950/10 dark:shadow-black/40">
            <div className="mb-6 flex items-center justify-between border-b border-border pb-4">
              <div>
                <p className="text-sm text-muted-foreground">Workspace preview</p>
                <h2 className="text-xl font-semibold">Operations hub</h2>
              </div>
              <span className="rounded-full bg-muted px-3 py-1 text-xs font-medium text-muted-foreground">
                Shell only
              </span>
            </div>
            <div className="space-y-3" aria-label="Example shell cards">
              {[
                "Workspace-aware navigation",
                "Accessible command palette",
                "React Query provider ready",
              ].map((item) => (
                <div
                  key={item}
                  className="rounded-lg border border-border bg-background p-4 text-sm font-medium"
                >
                  {item}
                </div>
              ))}
            </div>
          </div>
        </div>
        <footer className="border-t border-border pt-6 text-sm text-muted-foreground">
          <p>
            <Link className="underline underline-offset-4 hover:text-foreground" href="/legal">
              Legal, licence, and source code information
            </Link>
          </p>
        </footer>
      </section>
    </main>
  );
}
