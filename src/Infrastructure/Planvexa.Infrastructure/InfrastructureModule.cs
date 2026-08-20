namespace Planvexa.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Infrastructure.Persistence.Interceptors;
using Planvexa.Infrastructure.Persistence.Repositories;
using Planvexa.Modules.Audit.Application;
using Planvexa.Modules.Identity.Application;
using Planvexa.Modules.Tenancy.Application;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString, string? maintenanceConnectionString = null)
    {
        // Cross-cutting primitives.
        services.AddSingleton(new MaintenanceConnection(maintenanceConnectionString));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();

        // Workspace context abstraction (ADR 0015) — the sole isolation/authorization boundary,
        // populated by WorkspaceResolutionMiddleware once a Workspace is resolved.
        services.AddScoped<IWorkspaceContextAccessor, WorkspaceContextAccessor>();

        // RLS session variable interceptor (resolved per scope so it sees the current workspace).
        services.AddScoped<WorkspaceConnectionInterceptor>();

        services.AddDbContext<PlanvexaDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(sp.GetRequiredService<WorkspaceConnectionInterceptor>());

            var efLogPath = Environment.GetEnvironmentVariable("PLANVEXA_EF_LOG");
            if (!string.IsNullOrWhiteSpace(efLogPath))
            {
                options.EnableSensitiveDataLogging();
                options.LogTo(
                    msg => System.IO.File.AppendAllText(efLogPath, msg + Environment.NewLine),
                    Microsoft.Extensions.Logging.LogLevel.Information);
            }
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PlanvexaDbContext>());

        // Stores.
        services.AddScoped<IWorkspaceStore, WorkspaceStore>();
        services.AddScoped<IMembershipStore, MembershipStore>();
        services.AddScoped<IInvitationStore, InvitationStore>();
        services.AddScoped<ITeamStore, TeamStore>();
        services.AddScoped<IFeatureEntitlementStore, FeatureEntitlementStore>();
        services.AddScoped<IRoleStore, RoleStore>();
        services.AddScoped<IResourcePermissionStore, ResourcePermissionStore>();
        services.AddScoped<IUserStore, UserStore>();
        services.AddScoped<IAuditStore, AuditStore>();

        // WorkManagement stores.
        services.AddScoped<Modules.WorkManagement.Application.ISpaceStore, SpaceStore>();
        services.AddScoped<Modules.WorkManagement.Application.IFolderStore, FolderStore>();
        services.AddScoped<Modules.WorkManagement.Application.ITaskListStore, TaskListStore>();
        services.AddScoped<Modules.WorkManagement.Application.IStatusSchemeStore, StatusSchemeStore>();
        services.AddScoped<Modules.WorkManagement.Application.ITagStore, TagStore>();
        services.AddScoped<Modules.WorkManagement.Application.IWorkItemStore, WorkItemStore>();
        services.AddScoped<Modules.WorkManagement.Application.IDependencyStore, DependencyStore>();
        services.AddScoped<Modules.WorkManagement.Application.IChecklistStore, ChecklistStore>();
        services.AddScoped<Modules.WorkManagement.Application.ICustomFieldStore, CustomFieldStore>();
        services.AddScoped<Modules.WorkManagement.Application.IRecurringTaskStore, RecurringTaskStore>();
        services.AddScoped<Modules.WorkManagement.Application.ISavedViewStore, SavedViewStore>();
        services.AddScoped<Modules.WorkManagement.Application.IActivityStore, ActivityStore>();
        services.AddScoped<Modules.WorkManagement.Application.IReminderStore, ReminderStore>();
        services.AddScoped<Modules.WorkManagement.Application.IAttachmentStore, AttachmentStore>();
        services.AddScoped<Modules.WorkManagement.Application.ISearchStore, SearchStore>();
        services.AddScoped<Modules.WorkManagement.Application.IWorkTemplateStore, WorkTemplateStore>();
        services.AddScoped<Modules.WorkManagement.Application.IWorkFavoriteStore, WorkFavoriteStore>();
        services.AddScoped<Modules.WorkManagement.Application.IRecentItemStore, RecentItemStore>();
        services.AddScoped<Modules.WorkManagement.Application.IMyWorkPreferenceStore, MyWorkPreferenceStore>();

        // Importer stores.
        services.AddScoped<Modules.WorkManagement.Application.IImportJobStore, ImportJobStore>();
        services.AddScoped<Modules.WorkManagement.Application.IImportJobRowStore, ImportJobRowStore>();

        // Task-management completeness stores.
        services.AddScoped<Modules.WorkManagement.Application.ITaskListMembershipStore, TaskListMembershipStore>();
        services.AddScoped<Modules.WorkManagement.Application.ITaskTypeStore, TaskTypeStore>();
        services.AddScoped<Modules.WorkManagement.Application.ITaskRelationStore, TaskRelationStore>();

        // Collaboration stores.
        services.AddScoped<Modules.Collaboration.Application.ICommentStore, CommentStore>();
        services.AddScoped<Modules.Collaboration.Application.ICommentAttachmentStore, CommentAttachmentStore>();
        services.AddScoped<Modules.Collaboration.Application.IShareLinkStore, ShareLinkStore>();
        services.AddScoped<Modules.Collaboration.Application.IPublicCommentStore, PublicCommentStore>();

        // Notifications stores.
        services.AddScoped<Modules.Notifications.Application.INotificationStore, NotificationStore>();
        services.AddScoped<Modules.Notifications.Application.INotificationPreferenceStore, NotificationPreferenceStore>();
        services.AddScoped<Modules.Notifications.Application.INotificationDeliveryStore, NotificationDeliveryStore>();
        services.AddScoped<Modules.Notifications.Application.IDigestPreferenceStore, DigestPreferenceStore>();

        // TimeTracking stores.
        services.AddScoped<Modules.TimeTracking.Application.ITimeEntryStore, TimeEntryStore>();
        services.AddScoped<Modules.TimeTracking.Application.ITimePolicyStore, TimePolicyStore>();
        services.AddScoped<Modules.TimeTracking.Application.IMemberRateStore, MemberRateStore>();
        services.AddScoped<Modules.TimeTracking.Application.ITimesheetStore, TimesheetStore>();
        services.AddScoped<Modules.TimeTracking.Application.ITimeTagStore, TimeTagStore>();
        services.AddScoped<Modules.TimeTracking.Application.IBudgetStore, BudgetStore>();

        // Planning stores.
        services.AddScoped<Modules.Planning.Application.IWorkScheduleStore, WorkScheduleStore>();
        services.AddScoped<Modules.Planning.Application.IHolidayStore, HolidayStore>();
        services.AddScoped<Modules.Planning.Application.ILeaveStore, LeaveStore>();
        services.AddScoped<Modules.Planning.Application.IEstimateStore, EstimateStore>();
        services.AddScoped<Modules.Planning.Application.ISprintStore, SprintStore>();

        // Reporting stores.
        services.AddScoped<Modules.Reporting.Application.IDashboardStore, DashboardStore>();
        services.AddScoped<Modules.Reporting.Application.IPortfolioStore, PortfolioStore>();
        services.AddScoped<Modules.Reporting.Application.IRiskStore, RiskStore>();
        services.AddScoped<Modules.Reporting.Application.IScheduledReportStore, ScheduledReportStore>();

        // Cross-module reporting query contracts. Implemented here because they read across
        // WorkManagement/TimeTracking tables; the ambient tenant query filter provides isolation.
        services.AddScoped<SharedContracts.Reporting.IWorkReportingQueries, WorkReportingQueries>();
        services.AddScoped<SharedContracts.Reporting.ITimeReportingQueries, TimeReportingQueries>();
        services.AddScoped<SharedContracts.Reporting.IGoalReportingQueries, GoalReportingQueries>();

        // Documents stores.
        services.AddScoped<Modules.Documents.Application.IDocumentStore, DocumentStore>();
        services.AddScoped<Modules.Documents.Application.IDocumentTemplateStore, DocumentTemplateStore>();
        services.AddScoped<Modules.Documents.Application.IDocumentCommentStore, DocumentCommentStore>();
        services.AddScoped<Modules.Documents.Application.IDocumentShareLinkStore, DocumentShareLinkStore>();

        // Forms stores (IFormUploadStore added later).
        services.AddScoped<Modules.Forms.Application.IFormStore, FormStore>();
        services.AddScoped<Modules.Forms.Application.IFormSubmissionStore, FormSubmissionStore>();
        services.AddScoped<Modules.Forms.Application.IFormUploadStore, FormUploadStore>();

        // Automations stores (including rule versioning).
        services.AddScoped<Modules.Automations.Application.IAutomationRuleStore, AutomationRuleStore>();
        services.AddScoped<Modules.Automations.Application.IAutomationRunStore, AutomationRunStore>();
        services.AddScoped<Modules.Automations.Application.IAutomationRuleVersionStore, AutomationRuleVersionStore>();

        // Integrations stores.
        services.AddScoped<Modules.Integrations.Application.IWebhookSubscriptionStore, WebhookSubscriptionStore>();
        services.AddScoped<Modules.Integrations.Application.IWebhookDeliveryStore, WebhookDeliveryStore>();
        services.AddScoped<Modules.Integrations.Application.IPersonalAccessTokenStore, PersonalAccessTokenStore>();

        // Integrations stores (OAuth applications/tokens + provider settings).
        services.AddScoped<Modules.Integrations.Application.IOAuthApplicationStore, OAuthApplicationStore>();
        services.AddScoped<Modules.Integrations.Application.IOAuthAuthorizationCodeStore, OAuthAuthorizationCodeStore>();
        services.AddScoped<Modules.Integrations.Application.IOAuthTokenStore, OAuthTokenStore>();
        services.AddScoped<Modules.Integrations.Application.IIntegrationProviderSettingsStore, IntegrationProviderSettingsStore>();

        // Governance stores + query contracts.
        services.AddScoped<Modules.Governance.Application.ISecuritySettingsStore, SecuritySettingsStore>();
        services.AddScoped<Modules.Governance.Application.IExportJobStore, ExportJobStore>();
        services.AddScoped<Modules.Governance.Application.IWorkspaceIpAllowRuleStore, WorkspaceIpAllowRuleStore>();
        services.AddScoped<SharedContracts.Governance.IAuditQuery, AuditQuery>();
        services.AddScoped<SharedContracts.Governance.IExportDataSource, ExportDataSource>();

        // AI stores + content sources (Document/Chat content sources added later).
        services.AddScoped<Modules.Ai.Application.IAiRequestStore, AiRequestStore>();
        services.AddScoped<Modules.Ai.Application.IAiProviderSettingsStore, AiProviderSettingsStore>();
        services.AddScoped<SharedContracts.Ai.IAiTaskContentSource, AiTaskContentSource>();
        services.AddScoped<SharedContracts.Ai.IAiDocumentContentSource, AiDocumentContentSource>();
        services.AddScoped<SharedContracts.Ai.IAiChatContentSource, AiChatContentSource>();
        services.AddScoped<SharedContracts.Ai.IAiFeatureGate, AiFeatureGate>();

        // Mobile stores + change feed.
        services.AddScoped<Modules.Mobile.Application.IDeviceRegistrationStore, DeviceRegistrationStore>();
        services.AddScoped<SharedContracts.Mobile.IChangeFeed, ChangeFeed>();
        services.AddScoped<SharedContracts.Mobile.IPushDeviceDirectory, PushDeviceDirectory>();

        // Chat stores.
        services.AddScoped<Modules.Chat.Application.IChatChannelStore, ChatChannelStore>();
        services.AddScoped<Modules.Chat.Application.IChatMessageStore, ChatMessageStore>();
        services.AddScoped<Modules.Chat.Application.IChatAttachmentStore, ChatAttachmentStore>();
        services.AddScoped<Modules.Chat.Application.IChatChannelReadStateStore, ChatChannelReadStateStore>();

        // Goals stores.
        services.AddScoped<Modules.Goals.Application.IGoalStore, GoalStore>();
        services.AddScoped<Modules.Goals.Application.IGoalFolderStore, GoalFolderStore>();
        services.AddScoped<Modules.Goals.Application.IGoalCommentStore, GoalCommentStore>();

        // Whiteboards stores + the Task/Document linked-resource ACL bridge Clips
        // also uses.
        services.AddScoped<Modules.Whiteboards.Application.IWhiteboardStore, WhiteboardStore>();
        services.AddScoped<Modules.Whiteboards.Application.IWhiteboardTemplateStore, WhiteboardTemplateStore>();
        services.AddScoped<Modules.Whiteboards.Application.IWhiteboardCollabStateStore, WhiteboardCollabStateStore>();
        services.AddScoped<SharedContracts.Workspaces.ILinkedResourceAccessQuery, LinkedResourceAccessQuery>();

        // Clips stores.
        services.AddScoped<Modules.Clips.Application.IClipStore, ClipStore>();
        services.AddScoped<Modules.Clips.Application.IClipCommentStore, ClipCommentStore>();
        services.AddScoped<Modules.Clips.Application.IClipTranscriptStore, ClipTranscriptStore>();

        // Retention (Governance): policy store + purger.
        services.AddScoped<Modules.Governance.Application.IRetentionPolicyStore, RetentionPolicyStore>();
        services.AddScoped<SharedContracts.Governance.IRetentionPurger, RetentionPurger>();

        // GDPR-style user-data export/deletion: cross-module read of a user's own
        // tasks/comments/time entries/memberships + cross-module PAT hard-delete, same shape as the
        // Governance export/audit query contracts above.
        services.AddScoped<SharedContracts.UserData.IUserDataQuery, UserDataQuery>();
        services.AddScoped<SharedContracts.UserData.IUserDataEraser, UserDataQuery>();

        // Realtime notifier default (no-op). The API host overrides this with a SignalR implementation.
        services.AddScoped<BuildingBlocks.Abstractions.IRealtimeNotifier, BuildingBlocks.Abstractions.NullRealtimeNotifier>();

        // Workspace resolution.
        services.AddScoped<WorkspaceResolver>();
        services.AddScoped<IWorkspaceResolver>(sp => sp.GetRequiredService<WorkspaceResolver>());

        // Host administration (instance-level). Lives here rather than in a module because it
        // deliberately spans Tenancy + Identity + Audit, which the module boundary rules forbid a
        // module from doing — see HostAdminQueries' class doc comment.
        services.AddScoped<HostAdmin.HostAdminQueries>();
        services.AddScoped<HostAdmin.HostAdminActionService>();

        // Installation-wide settings. The cache is a singleton (process-wide memo); the service that
        // fills it is scoped like everything else that touches the DbContext.
        // Default: Planvexa controls only its own half of self-registration. The API host replaces this
        // with the Keycloak-managing implementation when identity-provider admin credentials are
        // configured (registered later, so it wins the resolve).
        services.AddScoped<SharedContracts.Platform.IIdentityProviderRegistration, Platform.UnmanagedIdentityProviderRegistration>();

        services.AddSingleton<Platform.InstanceSettingsCache>();
        services.AddScoped<Platform.InstanceSettingsService>();
        services.AddScoped<SharedContracts.Platform.IInstanceSettingsProvider>(
            sp => sp.GetRequiredService<Platform.InstanceSettingsService>());

        // Read side of the instance log store. The write side is an ILoggerProvider in the API host —
        // it is a singleton draining a channel and deliberately does not go through the DbContext.
        services.AddScoped<Platform.InstanceLogQueries>();

        return services;
    }
}
