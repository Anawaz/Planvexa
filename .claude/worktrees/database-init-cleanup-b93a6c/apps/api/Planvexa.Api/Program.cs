using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Planvexa.Api.Auth;
using Planvexa.Api.Endpoints;
using Planvexa.Api.Middleware;
using Planvexa.Api.Notifications;
using Planvexa.Api.Outbox;
using Planvexa.Api.Realtime;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.Database;
using Planvexa.Infrastructure;
using Planvexa.Modules.Audit;
using Planvexa.Modules.Collaboration;
using Planvexa.Modules.Identity;
using Planvexa.Modules.Notifications;
using Planvexa.Modules.Tenancy;
using Planvexa.Modules.TimeTracking;
using Planvexa.Modules.WorkManagement;
using Planvexa.Modules.Planning;
using Planvexa.Modules.Reporting;
using Planvexa.Modules.Documents;
using Planvexa.Modules.Forms;
using Planvexa.Modules.Automations;
using Planvexa.Modules.Integrations;
using Planvexa.Modules.Governance;
using Planvexa.Modules.Ai;
using Planvexa.Modules.Mobile;
using Planvexa.Modules.Chat;
using Planvexa.Modules.Goals;
using Planvexa.Modules.Whiteboards;
using Planvexa.Modules.Clips;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var useDevAuth = builder.Configuration.GetValue<bool>("Authentication:UseDevelopmentHeaders");

if (useDevAuth && builder.Environment.IsProduction())
{
    throw new InvalidOperationException("Authentication:UseDevelopmentHeaders cannot be enabled in Production.");
}

var connectionString = builder.Configuration.GetConnectionString("Planvexa")
    ?? "Host=localhost;Port=5432;Database=planvexa;Username=planvexa;Password=planvexa";

// Optional privileged connection for cross-tenant background work (see MaintenanceConnection).
var maintenanceConnectionString = builder.Configuration.GetConnectionString("PlanvexaMaintenance");

// ---- Infrastructure & modules ----
builder.Services.AddInfrastructure(connectionString, maintenanceConnectionString);
builder.Services.AddIdentityModule();
builder.Services.AddAuditModule();
builder.Services.AddTenancyModule();
builder.Services.AddWorkManagementModule();
builder.Services.AddCollaborationModule();
builder.Services.AddNotificationsModule();
builder.Services.AddTimeTrackingModule();
builder.Services.AddPlanningModule();
builder.Services.AddReportingModule();
builder.Services.AddDocumentsModule();
builder.Services.AddFormsModule();
builder.Services.AddAutomationsModule();
builder.Services.AddIntegrationsModule();
builder.Services.AddGovernanceModule();
builder.Services.AddAiModule();
builder.Services.AddMobileModule();
builder.Services.AddChatModule();
builder.Services.AddGoalsModule();
builder.Services.AddWhiteboardsModule();
builder.Services.AddClipsModule();

// ---- Cross-module search fan-out ----
builder.Services.AddScoped<Planvexa.Api.Search.SearchAggregator>();

// ---- AI-ranked semantic search + workspace Q&A: both build on the fan-out above, never a
// parallel unfiltered retrieval path — see each class's doc comment for the security rationale. ----
builder.Services.AddScoped<Planvexa.Api.Search.SemanticSearchService>();
builder.Services.AddScoped<Planvexa.Api.Ai.WorkspaceQaService>();

// ---- AI completion provider ----
// LiteLLM/OpenAI-compatible per tenant (configured under Settings → AI), with the deterministic offline
// provider as the fallback for tenants that have not configured or enabled one.
builder.Services.AddScoped<Planvexa.Api.Ai.DeterministicAiCompletionProvider>();
builder.Services.AddScoped<Planvexa.Modules.Ai.Application.IAiSecretProtector, Planvexa.Api.Ai.DataProtectionAiSecretProtector>();
builder.Services.AddHttpClient(Planvexa.Api.Ai.LiteLlmCompletionProvider.ClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<Planvexa.Api.Ai.LiteLlmCompletionProvider>();
builder.Services.AddScoped<Planvexa.SharedContracts.Ai.IAiCompletionProvider>(sp => sp.GetRequiredService<Planvexa.Api.Ai.LiteLlmCompletionProvider>());
builder.Services.AddScoped<Planvexa.SharedContracts.Ai.IAiProviderProbe>(sp => sp.GetRequiredService<Planvexa.Api.Ai.LiteLlmCompletionProvider>());

// ---- Clip transcription ----
// Reuses the same per-workspace AiProviderSettings as chat completions above, calling the sibling
// Whisper-compatible /audio/transcriptions endpoint instead — see ClipTranscriptionProvider's doc comment.
// A longer timeout than chat completions: audio uploads can be large and transcription is slower than a
// short chat completion.
builder.Services.AddHttpClient(Planvexa.Api.Ai.ClipTranscriptionProvider.ClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddScoped<Planvexa.SharedContracts.Ai.IClipTranscriber, Planvexa.Api.Ai.ClipTranscriptionProvider>();

// ---- Attachment blob storage (local disk by default; S3-compatible, incl. MinIO for
// local dev, via FileStorage:Provider = "S3") ----
if (string.Equals(builder.Configuration["FileStorage:Provider"], "S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IFileStorage, Planvexa.Api.Storage.S3FileStorage>();
}
else
{
    builder.Services.AddSingleton<IFileStorage, Planvexa.Api.Storage.LocalDiskFileStorage>();
}

// ---- Malware scanning: no-op pass-through today, see NoOpMalwareScanner's doc comment
// for the ClamAV integration point every upload path is already wired to call. ----
builder.Services.AddSingleton<Planvexa.BuildingBlocks.Abstractions.IMalwareScanner, Planvexa.Api.Storage.NoOpMalwareScanner>();

// ---- Realtime (SignalR) + presence ----
builder.Services.AddSignalR();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

// ---- Notification delivery (email sender + background drain) ----
builder.Services.AddSingleton<SentEmailLog>();
if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Host"]))
{
    // Development only: real SMTP (Mailpit). Testing keeps LoggingEmailSender so tests assert via SentEmailLog.
    builder.Services.AddScoped<Planvexa.Modules.Notifications.Application.IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddScoped<Planvexa.Modules.Notifications.Application.IEmailSender, LoggingEmailSender>();
}

// Automations "email" action: same concrete sender, exposed via the cross-module contract so
// Automations can send email without depending on the Notifications module (AGENTS.md rule 7).
builder.Services.AddScoped<Planvexa.SharedContracts.Notifications.IEmailSender>(
    sp => (Planvexa.SharedContracts.Notifications.IEmailSender)sp.GetRequiredService<Planvexa.Modules.Notifications.Application.IEmailSender>());

// Invitation delivery (signed email link). Raw tokens leave the process only here, never via the API.
if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Host"]))
{
    builder.Services.AddScoped<Planvexa.Modules.Tenancy.Application.IInvitationEmailSender, SmtpInvitationEmailSender>();
}
else
{
    builder.Services.AddScoped<Planvexa.Modules.Tenancy.Application.IInvitationEmailSender, LoggingInvitationEmailSender>();
}
builder.Services.AddHostedService<NotificationDeliveryBackgroundService>();

// Push delivery: documented gap — see LoggingPushSender's doc comment for what a real
// Web Push (VAPID) or FCM/APNs sender needs that this codebase does not yet have.
builder.Services.AddSingleton<SentPushLog>();
builder.Services.AddScoped<Planvexa.Modules.Notifications.Application.IPushSender, LoggingPushSender>();

// VAPID keypair (see VapidKeyProvider's doc comment): one per process, ephemeral, exposed read-only via
// GET /api/v1/mobile/push/vapid-public-key.
builder.Services.AddSingleton<VapidKeyProvider>();

// Digest scheduler: daily/weekly activity-digest emails, permission-filtered at compile time.
builder.Services.AddHostedService<DigestBackgroundService>();

// Scheduled report delivery: periodic Dashboard export emailed to configured recipients.
if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Host"]))
{
    builder.Services.AddScoped<Planvexa.SharedContracts.Notifications.IReportEmailSender, Planvexa.Api.Reporting.SmtpReportEmailSender>();
}
else
{
    builder.Services.AddScoped<Planvexa.SharedContracts.Notifications.IReportEmailSender, Planvexa.Api.Reporting.LoggingReportEmailSender>();
}

builder.Services.AddHostedService<Planvexa.Api.Reporting.ScheduledReportBackgroundService>();

// Missing-time reminder scheduler: per-workspace cadence configured on TimePolicy.
builder.Services.AddHostedService<MissingTimeReminderBackgroundService>();

// Automations expansion: due-date/scheduled/SLA sweeps for trigger types that aren't discrete
// events, plus bounded retry-with-backoff for Failed automation runs.
builder.Services.AddHostedService<Planvexa.Api.Automations.DueDateBackgroundService>();
builder.Services.AddHostedService<Planvexa.Api.Automations.ScheduledAutomationBackgroundService>();
builder.Services.AddHostedService<Planvexa.Api.Automations.SlaBackgroundService>();
builder.Services.AddHostedService<Planvexa.Api.Automations.AutomationRetryBackgroundService>();

// ---- Current user (scoped, populated by middleware) ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());

// ---- Validation ----
builder.Services.AddValidatorsFromAssemblyContaining<CreateWorkspaceRequestValidator>();

// ---- Errors as RFC 9457 Problem Details ----
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// ---- Authentication / Authorization ----
if (useDevAuth)
{
    builder.Services
        .AddAuthentication(DevAuthenticationHandler.SchemeName)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAuthenticationHandler>(
            DevAuthenticationHandler.SchemeName, _ => { });
}
else
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = builder.Configuration["Keycloak:Authority"];
            options.Audience = builder.Configuration["Keycloak:Audience"];
            // Containers that talk to an in-cluster/plain-HTTP identity provider can opt out without
            // pretending to be a Development environment (Authentication__RequireHttpsMetadata=false).
            options.RequireHttpsMetadata = builder.Configuration.GetValue(
                "Authentication:RequireHttpsMetadata", !builder.Environment.IsDevelopment());

            // Browsers cannot set an Authorization header on a WebSocket handshake, so SignalR clients
            // pass the access token as ?access_token=. Only honoured for /hubs paths.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/hubs"))
                    {
                        var token = context.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrEmpty(token))
                        {
                            context.Token = token;
                        }
                    }

                    return Task.CompletedTask;
                },
            };
        });
}

builder.Services.AddAuthorization();

// ---- JSON: serialize enums as their names (stable public API contract) ----
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// ---- Rate limiting (per client IP fixed window) ----
var rateLimitPermits = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? 1000 : 100;
var formSubmitPermits = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? 1000 : 20;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitPermits,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0,
            }));

    // A tighter, form-specific policy for the anonymous public submission endpoint —
    // per (client IP, form token) so one noisy form/bot can't exhaust another form's or client's budget,
    // reusing the same AddRateLimiter infra as the global limiter above rather than new infrastructure.
    options.AddPolicy("form-submission", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: (context.Connection.RemoteIpAddress?.ToString() ?? "anonymous") + ":" + (context.Request.RouteValues["token"] as string ?? "-"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = formSubmitPermits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// ---- OpenAPI ----
builder.Services.AddOpenApi();

// ---- Observability (OpenTelemetry) ----
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("planvexa-api"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    });

// ---- Outbox + workflow event pipeline (automations + webhooks) ----
builder.Services.AddSingleton<IIntegrationEventPublisher, WorkspaceEventDispatchingPublisher>();
builder.Services.AddHostedService<OutboxProcessor>();

// Webhook delivery (host-provided signed HTTP sender).
builder.Services.AddHttpClient(Planvexa.Api.Integrations.HttpWebhookSender.ClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<Planvexa.SharedContracts.Integrations.IWebhookSender, Planvexa.Api.Integrations.HttpWebhookSender>();

// Workspace-configured third-party provider settings (encrypted secret) + the two providers
// with a real, mockable HTTP call behind them (Slack incoming-webhook post, GitHub issue comment).
builder.Services.AddScoped<Planvexa.Modules.Integrations.Application.IIntegrationSecretProtector, Planvexa.Api.Integrations.DataProtectionIntegrationSecretProtector>();
builder.Services.AddHttpClient(Planvexa.Api.Integrations.SlackClient.ClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient(Planvexa.Api.Integrations.GitHubClient.ClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<Planvexa.Modules.Integrations.Application.Services.ISlackClient, Planvexa.Api.Integrations.SlackClient>();
builder.Services.AddScoped<Planvexa.Modules.Integrations.Application.Services.IGitHubClient, Planvexa.Api.Integrations.GitHubClient>();

// ---- Recurring task generation ----
builder.Services.AddHostedService<Planvexa.Api.Recurring.RecurringTaskBackgroundService>();
builder.Services.AddHostedService<Planvexa.Api.Recurring.ReminderBackgroundService>();

// ---- Governed export job processing ----
builder.Services.AddHostedService<Planvexa.Api.Governance.ExportJobBackgroundService>();

// ---- Data retention purge ----
builder.Services.AddHostedService<Planvexa.Api.Governance.RetentionBackgroundService>();

var app = builder.Build();

// ---- Database deployment (DbUp runs before hosted services process the outbox/jobs) ----
if (app.Configuration.GetValue("Database:RunDbUpOnStartup", true))
{
    PlanvexaDatabase.Upgrade(connectionString, message => app.Logger.LogInformation("{Message}", message));
}

if (app.Configuration.GetValue<bool>("Database:ResetDevelopmentData"))
{
    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException("Database:ResetDevelopmentData is only allowed in Development or Testing.");
    }

    await PlanvexaDevelopmentSeeder.ResetAsync(connectionString, message => app.Logger.LogWarning("{Message}", message));
}

var seedDevelopmentData = app.Configuration.GetValue<bool>("Database:SeedDevelopmentData");
await PlanvexaDevelopmentSeeder.SeedAsync(
    connectionString,
    seedDevelopmentData,
    message => app.Logger.LogInformation("{Message}", message));

// Runs on every environment: gives a freshly created database the one admin user and one workspace it
// needs to be usable at all. Defers to the demo seed when that ran — see PlanvexaBootstrap.
await Planvexa.Api.Startup.PlanvexaBootstrap.EnsureAdminWorkspaceAsync(app, connectionString, seedDevelopmentData);

// ---- Pipeline ----
app.UseExceptionHandler();
app.UseMiddleware<Planvexa.Api.Middleware.SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Health probes must answer even when the identity provider is unreachable or misconfigured:
// AuthenticationMiddleware resolves the JwtBearer options on every request, so a bad authority (e.g.
// plain HTTP with RequireHttpsMetadata on) turns /health/* into a 500. They are anonymous anyway, so
// branching authentication around them costs nothing and keeps the probes honest about the app, not
// the IdP.
//
// WebApplication injects its own UseAuthentication ahead of ALL user middleware unless this key is
// present — and UseAuthentication() sets it on the builder it is called on, which for a UseWhen branch
// is a copy. Setting it here is what makes the branch below the only registration that runs.
// Health_probes_survive_a_misconfigured_jwt_authority fails if a future framework version renames it.
((IApplicationBuilder)app).Properties["__AuthenticationMiddlewareSet"] = true;
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/health"),
    branch => branch.UseAuthentication());

app.UseMiddleware<PatAuthenticationMiddleware>();
app.UseMiddleware<Planvexa.Api.Middleware.OAuthAuthenticationMiddleware>();
app.UseMiddleware<UserContextMiddleware>();
app.UseMiddleware<WorkspaceResolutionMiddleware>();
app.UseMiddleware<Planvexa.Api.Middleware.IpAllowListMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<Planvexa.Api.Middleware.OAuthScopeEnforcementMiddleware>();

app.MapPlanvexaEndpoints();
app.MapHub<WorkspaceHub>("/hubs/workspace");

await app.RunAsync();

/// <summary>Exposed so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;



