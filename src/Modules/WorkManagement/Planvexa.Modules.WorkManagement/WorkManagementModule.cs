namespace Planvexa.Modules.WorkManagement;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Application.Services;

/// <summary>
/// Composition marker + DI registration for the WorkManagement module. Store implementations and the
/// default-scheme provisioning are supplied by the Infrastructure project; entity configurations are
/// discovered by scanning this assembly.
/// </summary>
public static class WorkManagementModule
{
    public const string Schema = "work";

    public static IServiceCollection AddWorkManagementModule(this IServiceCollection services)
    {
        services.AddScoped<WorkServiceContext>();
        services.AddScoped<WorkspaceProvisioningService>();
        services.AddScoped<SpaceService>();
        services.AddScoped<FolderService>();
        services.AddScoped<TaskListService>();
        services.AddScoped<StatusSchemeService>();
        services.AddScoped<TagService>();
        services.AddScoped<WorkItemService>();
        services.AddScoped<ReminderService>();
        services.AddScoped<DependencyService>();
        services.AddScoped<ChecklistService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<CustomFieldService>();
        services.AddScoped<RecurringTaskService>();
        services.AddScoped<SavedViewService>();
        services.AddScoped<ViewQueryService>();
        services.AddScoped<WorkspaceActivityService>();
        services.AddScoped<SearchService>();
        services.AddScoped<Planvexa.SharedContracts.Search.ISearchProvider>(sp => sp.GetRequiredService<SearchService>());
        services.AddScoped<ResourceSharingService>();
        services.AddScoped<WorkTemplateService>();
        services.AddScoped<WorkFavoriteService>();
        services.AddScoped<RecentItemService>();
        services.AddScoped<MyWorkPreferenceService>();
        services.AddScoped<TaskTypeService>();
        services.AddScoped<Planvexa.SharedContracts.Work.ITaskDirectory, TaskDirectory>();
        services.AddScoped<Planvexa.SharedContracts.Work.ITaskWriteApi, TaskWriteApi>();
        services.AddScoped<Planvexa.SharedContracts.Workspaces.IWorkspaceProvisioner, WorkspaceDefaultsProvisioner>();

        // Registered both as itself (WorkServiceContext's cheap ancestor-privacy probe binds to this
        // concrete type, not the shared interface — see WorkServiceContext.Hierarchy doc comment) and as
        // the shared contract Tenancy's cross-module resolver enumerates.
        services.AddScoped<WorkResourceHierarchyQuery>();
        services.AddScoped<Planvexa.SharedContracts.Workspaces.IResourceHierarchyQuery>(sp => sp.GetRequiredService<WorkResourceHierarchyQuery>());

        // Bulk data importers (CSV/Excel fully; Trello fully; Jira/Asana/ClickUp extension points only —
        // see UnimplementedImportSources.cs).
        services.AddScoped<Planvexa.Modules.WorkManagement.Application.Importers.IImportSource, Planvexa.Modules.WorkManagement.Application.Importers.CsvImportSource>();
        services.AddScoped<Planvexa.Modules.WorkManagement.Application.Importers.IImportSource, Planvexa.Modules.WorkManagement.Application.Importers.XlsxImportSource>();
        services.AddScoped<Planvexa.Modules.WorkManagement.Application.Importers.IImportSource, Planvexa.Modules.WorkManagement.Application.Importers.TrelloImportSource>();
        services.AddScoped<Planvexa.Modules.WorkManagement.Application.Importers.IImportSource, Planvexa.Modules.WorkManagement.Application.Importers.JiraImportSource>();
        services.AddScoped<Planvexa.Modules.WorkManagement.Application.Importers.IImportSource, Planvexa.Modules.WorkManagement.Application.Importers.AsanaImportSource>();
        services.AddScoped<Planvexa.Modules.WorkManagement.Application.Importers.IImportSource, Planvexa.Modules.WorkManagement.Application.Importers.ClickUpImportSource>();
        services.AddScoped<ImportJobService>();
        services.AddScoped<DuplicateTaskService>();
        return services;
    }
}
