var builder = DistributedApplication.CreateBuilder(args);

string Config(string key, string env, string fallback) => builder.Configuration[key] ?? Environment.GetEnvironmentVariable(env) ?? fallback;

// Dev defaults live in appsettings.Development.json; user secrets and ConnectionStrings__* env vars override.
var planvexaConnectionString = builder.AddConnectionString("Planvexa");
var keycloakDatabase = Config("Keycloak:DatabaseName", "KEYCLOAK_DB_NAME", "keycloak");
var keycloakUrl = "http://localhost:8081";
var webUrl = Config("Web:Url", "PLANVEXA_WEB_URL", "http://localhost:3000");

// Creates the 'planvexa' and 'keycloak' databases if they are missing, so a fresh clone can F5 without
// running dev-up first. Same script dev-up.ps1 uses. It never touches the PostgreSQL server itself.
var databases = builder.AddExecutable("db-bootstrap", "pwsh", "../..", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/ensure-databases.ps1")
    .WithEnvironment("ConnectionStrings__Planvexa", planvexaConnectionString)
    .WithEnvironment("KEYCLOAK_DB_NAME", keycloakDatabase);

// Fresh clones have no node_modules and `npm run dev` would fail. ponytail: presence check only, like
// dev-up.ps1 -- delete apps/web/node_modules after a dependency bump to force a reinstall.
//
// It also drops a poisoned .next first. `next dev` (Turbopack) and `next build` share apps/web/.next
// because next.config.ts sets no distDir, so running a production build while a dev server is up
// leaves mixed artifacts, and the next dev server reuses them -- serving stale modules whose exports
// no longer match source ("<symbol> is not a function", with a __TURBOPACK__imported__module__ prefix).
// BUILD_ID is the precise marker: `next dev` never writes it, `next build` always does. Keying on it
// rather than clearing unconditionally keeps the warm cache (and fast startup) on every normal launch.
// Only healed at startup -- a `next build` run mid-session still poisons that session until restart.
var webInstall = builder.AddExecutable("web-install", "pwsh", "../web", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
    "$ErrorActionPreference='Stop'; if (Test-Path .next/BUILD_ID) { Write-Host 'Removing .next: it holds production build output, which poisons the dev module graph'; Remove-Item -Recurse -Force .next }; if (Test-Path node_modules) { Write-Host 'node_modules present; skipping npm install'; exit 0 }; if (Test-Path package-lock.json) { npm ci } else { npm install }; exit $LASTEXITCODE");

var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "latest")
    .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp")
    .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "http")
    .WithHttpHealthCheck("/", endpointName: "http");

// S3-compatible object storage for local dev. Always provisioned here (same as
// mailpit/jaeger below, which start regardless of whether a given dev session exercises email/tracing) --
// but FileStorage:Provider still defaults to "LocalDisk" a few lines down, so a dev workflow that never
// touches this container is completely unaffected; set FileStorage:Provider = "S3" (config/user-secrets)
// to actually exercise it.
var minioUser = Config("FileStorage:S3:AccessKey", "MINIO_ROOT_USER", "planvexa");
var minioPassword = Config("FileStorage:S3:SecretKey", "MINIO_ROOT_PASSWORD", "planvexa-dev-secret");
var minio = builder.AddContainer("minio", "minio/minio", "RELEASE.2025-04-08T15-41-24Z")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", minioUser)
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioPassword)
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithHttpHealthCheck("/minio/health/live", endpointName: "api");

var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one", "1.60")
    .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true")
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp")
    .WithHttpEndpoint(port: 16686, targetPort: 16686, name: "ui")
    .WithHttpHealthCheck("/", endpointName: "ui");

var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.4")
    .WithArgs("start-dev")
    .WaitForCompletion(databases)
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", Config("Keycloak:AdminUser", "KEYCLOAK_ADMIN_USER", "admin"))
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", Config("Keycloak:AdminPassword", "KEYCLOAK_ADMIN_PASSWORD", "admin"))
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_URL_HOST", Config("Keycloak:DatabaseHost", "KEYCLOAK_DB_HOST", "host.docker.internal"))
    .WithEnvironment("KC_DB_URL_PORT", Config("Keycloak:DatabasePort", "KEYCLOAK_DB_PORT", "5432"))
    .WithEnvironment("KC_DB_URL_DATABASE", keycloakDatabase)
    .WithEnvironment("KC_DB_USERNAME", Config("Keycloak:DatabaseUsername", "KEYCLOAK_DB_USERNAME", "planvexa"))
    .WithEnvironment("KC_DB_PASSWORD", Config("Keycloak:DatabasePassword", "KEYCLOAK_DB_PASSWORD", "planvexa"))
    .WithHttpEndpoint(port: 8081, targetPort: 8080, name: "http")
    // Healthy means "the realm the API and web are configured against exists", not merely "the process
    // started" -- which is why keycloak-bootstrap below waits for start rather than for health.
    .WithHttpHealthCheck("/realms/planvexa", endpointName: "http");

// WaitForStart, not WaitFor: keycloak only reports healthy once this script has created the realm.
// The script polls /realms/master itself before it does anything.
var keycloakBootstrap = builder.AddExecutable("keycloak-bootstrap", "pwsh", "../..", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "scripts/keycloak-bootstrap.ps1")
    .WaitForStart(keycloak)
    .WithEnvironment("KEYCLOAK_URL", keycloakUrl)
    .WithEnvironment("KEYCLOAK_ADMIN_USER", Config("Keycloak:AdminUser", "KEYCLOAK_ADMIN_USER", "admin"))
    .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", Config("Keycloak:AdminPassword", "KEYCLOAK_ADMIN_PASSWORD", "admin"))
    .WithEnvironment("KEYCLOAK_REALM", "planvexa")
    .WithEnvironment("KEYCLOAK_WEB_CLIENT_ID", "planvexa-web")
    .WithEnvironment("KEYCLOAK_API_CLIENT_ID", "planvexa-api")
    .WithEnvironment("PLANVEXA_WEB_URL", webUrl);

var api = builder.AddProject<Projects.Planvexa_Api>("api")
    .WithReference(planvexaConnectionString)
    .WaitForCompletion(databases)
    // Without this the API can serve before the realm/clients/users exist and the first login fails.
    .WaitForCompletion(keycloakBootstrap)
    .WaitFor(mailpit)
    .WaitFor(jaeger)
    .WaitFor(minio)
    // FileStorage:Provider stays "LocalDisk" by default (see minio's comment above) -- these S3 settings
    // are supplied either way so flipping the provider is a one-line config override, not a rewire.
    .WithEnvironment("FileStorage__Provider", Config("FileStorage:Provider", "PLANVEXA_FILESTORAGE_PROVIDER", "LocalDisk"))
    .WithEnvironment("FileStorage__S3__ServiceUrl", "http://localhost:9000")
    .WithEnvironment("FileStorage__S3__BucketName", Config("FileStorage:S3:BucketName", "PLANVEXA_FILESTORAGE_S3_BUCKET", "planvexa-dev"))
    .WithEnvironment("FileStorage__S3__AccessKey", minioUser)
    .WithEnvironment("FileStorage__S3__SecretKey", minioPassword)
    .WithEnvironment("FileStorage__S3__ForcePathStyle", "true")
    .WithEnvironment("Database__RunDbUpOnStartup", "true")
    .WithEnvironment("Database__SeedDevelopmentData", "true")
    .WithEnvironment("Database__ResetDevelopmentData", builder.Configuration["Database:ResetDevelopmentData"] ?? "false")
    // Privileged connection for cross-tenant background sweeps and credential-keyed lookups; see
    // MaintenanceConnection. Falls back to the application connection when the role does not exist.
    .WithEnvironment("ConnectionStrings__PlanvexaMaintenance", Config("ConnectionStrings:PlanvexaMaintenance", "ConnectionStrings__PlanvexaMaintenance", "Host=localhost;Port=5432;Database=planvexa;Username=planvexa_maint;Password=planvexa_maint"))
    .WithEnvironment("OpenTelemetry__OtlpEndpoint", "http://localhost:4317")
    // Mailpit's smtp endpoint is host-published on the fixed port above; literals match the rest of this file.
    .WithEnvironment("Smtp__Host", "localhost")
    .WithEnvironment("Smtp__Port", "1025")
    .WithEnvironment("Keycloak__Authority", $"{keycloakUrl}/realms/planvexa")
    .WithEnvironment("Keycloak__Audience", "planvexa-api")
    .WithHttpHealthCheck("/health/ready");

// Apps/collaboration, a Node/TypeScript Hocuspocus server for realtime document editing (Yjs).
// Node, not .NET, because Yjs/Hocuspocus's ecosystem is JS-native — see apps/collaboration's README-level
// comment in package.json. Same fresh-clone-friendly install-then-run pattern as web-install/web above.
var collabInstall = builder.AddExecutable("collab-install", "pwsh", "../collaboration", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
    "$ErrorActionPreference='Stop'; if (Test-Path node_modules) { Write-Host 'node_modules present; skipping npm install'; exit 0 }; if (Test-Path package-lock.json) { npm ci } else { npm install }; exit $LASTEXITCODE");

var collabPort = 1234;
var collaboration = builder.AddExecutable("collaboration", "npm", "../collaboration", "run", "dev")
    .WaitForCompletion(collabInstall)
    .WaitFor(api)
    .WithHttpEndpoint(port: collabPort, env: "PORT")
    // Calls back into the API's internal can-collaborate check on every WebSocket connection attempt —
    // see apps/collaboration/src/auth.ts and DocumentService.CanCollaborateAsync.
    .WithEnvironment("PLANVEXA_API_BASE_URL", "http://localhost:8080")
    // ponytail: a literal dev-default connection string rather than reusing the Aspire `planvexaConnectionString`
    // resource, since that resolves to Npgsql keyword=value format (Host=...;Port=...) and `pg.Pool`'s
    // `connectionString` option expects a postgres:// URI — reconciling the two formats generically wasn't
    // worth it for a single dev-only wiring point. Override with PLANVEXA_COLLAB_DB_URL for non-default
    // credentials, same as ConnectionStrings__PlanvexaMaintenance above.
    .WithEnvironment("PLANVEXA_COLLAB_DB_URL", Config("Collaboration:DatabaseUrl", "PLANVEXA_COLLAB_DB_URL", "postgresql://planvexa:planvexa@localhost:5432/planvexa"));

var collabWsUrl = $"ws://localhost:{collabPort}";

builder.AddExecutable("web", "npm", "../web", "run", "dev")
    .WithReference(api)
    .WaitForCompletion(webInstall)
    .WaitFor(api)
    .WaitFor(collaboration)
    .WithHttpEndpoint(port: 3000, env: "PORT")
    // Node's default 16 KB header ceiling 431s real browsers: localhost cookies are shared across
    // every port, so other dev tools' cookies stack on top of the ~5.5 KB chunked session.
    .WithEnvironment("NODE_OPTIONS", "--max-http-header-size=65536")
    .WithEnvironment("NEXT_PUBLIC_API_BASE_URL", "http://localhost:8080")
    .WithEnvironment("NEXT_PUBLIC_COLLAB_WS_URL", collabWsUrl)
    .WithEnvironment("NEXT_PUBLIC_KEYCLOAK_URL", keycloakUrl)
    .WithEnvironment("NEXT_PUBLIC_KEYCLOAK_REALM", "planvexa")
    .WithEnvironment("NEXT_PUBLIC_KEYCLOAK_CLIENT_ID", "planvexa-web")
    // Server-side (BFF) configuration: without these the Next route handlers silently fall back to
    // their own defaults, including a hardcoded session secret.
    .WithEnvironment("API_BASE_URL", "http://localhost:8080")
    .WithEnvironment("KEYCLOAK_URL", keycloakUrl)
    .WithEnvironment("KEYCLOAK_REALM", "planvexa")
    .WithEnvironment("KEYCLOAK_WEB_CLIENT_ID", "planvexa-web")
    .WithEnvironment("PLANVEXA_WEB_URL", webUrl)
    // Fixed dev-only secret on purpose: a per-start random value would invalidate every session
    // cookie on restart. Production must set PLANVEXA_WEB_SESSION_SECRET (session.ts throws without it).
    .WithEnvironment("PLANVEXA_WEB_SESSION_SECRET", Config("Web:SessionSecret", "PLANVEXA_WEB_SESSION_SECRET", "planvexa-development-session-secret"))
    .WithHttpHealthCheck("/login");

builder.Build().Run();
