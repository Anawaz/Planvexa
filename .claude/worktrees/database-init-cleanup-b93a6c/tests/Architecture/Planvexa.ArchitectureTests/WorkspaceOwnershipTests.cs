namespace Planvexa.ArchitectureTests;

using System.Reflection;
using Planvexa.BuildingBlocks.Domain;
using Shouldly;
using Xunit;

/// <summary>
/// Guards the Workspace ownership abstractions introduced for ADR 0015. Every <see cref="IWorkspaceOwned"/> entity must expose a non-static
/// <see cref="Guid"/> <c>WorkspaceId</c>. Also verifies the kernel keeps exposing the abstractions.
/// </summary>
public sealed class WorkspaceOwnershipTests
{
    private static readonly Assembly Kernel = typeof(Entity).Assembly;

    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(Modules.Identity.IdentityModule).Assembly,
        typeof(Modules.Tenancy.TenancyModule).Assembly,
        typeof(Modules.WorkManagement.WorkManagementModule).Assembly,
        typeof(Modules.Collaboration.CollaborationModule).Assembly,
        typeof(Modules.Notifications.NotificationsModule).Assembly,
        typeof(Modules.TimeTracking.TimeTrackingModule).Assembly,
        typeof(Modules.Planning.PlanningModule).Assembly,
        typeof(Modules.Reporting.ReportingModule).Assembly,
        typeof(Modules.Documents.DocumentsModule).Assembly,
        typeof(Modules.Forms.FormsModule).Assembly,
        typeof(Modules.Automations.AutomationsModule).Assembly,
        typeof(Modules.Integrations.IntegrationsModule).Assembly,
        typeof(Modules.Governance.GovernanceModule).Assembly,
        typeof(Modules.Ai.AiModule).Assembly,
        typeof(Modules.Mobile.MobileModule).Assembly,
        typeof(Modules.Chat.ChatModule).Assembly,
        typeof(Modules.Whiteboards.WhiteboardsModule).Assembly,
        typeof(Modules.Clips.ClipsModule).Assembly,
    ];

    [Fact]
    public void Kernel_exposes_workspace_ownership_abstractions()
    {
        Kernel.GetType("Planvexa.BuildingBlocks.Domain.IWorkspaceOwned").ShouldNotBeNull();
        Kernel.GetType("Planvexa.BuildingBlocks.Workspaces.IWorkspaceContext").ShouldNotBeNull();
        Kernel.GetType("Planvexa.BuildingBlocks.Workspaces.WorkspaceContext").ShouldNotBeNull();
    }

    [Fact]
    public void Workspace_owned_entities_expose_guid_workspace_id()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var owned = assembly.GetTypes()
                .Where(t => typeof(IWorkspaceOwned).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

            foreach (var type in owned)
            {
                var property = type.GetProperty(nameof(IWorkspaceOwned.WorkspaceId));
                property.ShouldNotBeNull($"{type.FullName} implements IWorkspaceOwned but has no WorkspaceId property.");
                property!.PropertyType.ShouldBe(typeof(Guid), $"{type.FullName}.WorkspaceId must be a Guid.");
            }
        }
    }
}
