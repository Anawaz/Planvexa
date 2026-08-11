namespace Planvexa.ArchitectureTests;

using System.Reflection;
using Shouldly;
using Xunit;

/// <summary>
/// Enforces the modular-monolith boundaries (AGENTS.md rule 7). Modules are independent bounded
/// contexts: they may depend on the shared kernel (BuildingBlocks) and cross-module contracts
/// (SharedContracts), but never on each other or on the composition/infrastructure layers.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly Identity = typeof(Modules.Identity.IdentityModule).Assembly;
    private static readonly Assembly Audit = typeof(Modules.Audit.AuditModule).Assembly;
    private static readonly Assembly Tenancy = typeof(Modules.Tenancy.TenancyModule).Assembly;
    private static readonly Assembly WorkManagement = typeof(Modules.WorkManagement.WorkManagementModule).Assembly;
    private static readonly Assembly Collaboration = typeof(Modules.Collaboration.CollaborationModule).Assembly;
    private static readonly Assembly Notifications = typeof(Modules.Notifications.NotificationsModule).Assembly;
    private static readonly Assembly TimeTracking = typeof(Modules.TimeTracking.TimeTrackingModule).Assembly;
    private static readonly Assembly Planning = typeof(Modules.Planning.PlanningModule).Assembly;
    private static readonly Assembly Reporting = typeof(Modules.Reporting.ReportingModule).Assembly;
    private static readonly Assembly Documents = typeof(Modules.Documents.DocumentsModule).Assembly;
    private static readonly Assembly Forms = typeof(Modules.Forms.FormsModule).Assembly;
    private static readonly Assembly Automations = typeof(Modules.Automations.AutomationsModule).Assembly;
    private static readonly Assembly Integrations = typeof(Modules.Integrations.IntegrationsModule).Assembly;
    private static readonly Assembly Governance = typeof(Modules.Governance.GovernanceModule).Assembly;
    private static readonly Assembly Ai = typeof(Modules.Ai.AiModule).Assembly;
    private static readonly Assembly Mobile = typeof(Modules.Mobile.MobileModule).Assembly;
    private static readonly Assembly Chat = typeof(Modules.Chat.ChatModule).Assembly;
    private static readonly Assembly Goals = typeof(Modules.Goals.GoalsModule).Assembly;
    private static readonly Assembly Whiteboards = typeof(Modules.Whiteboards.WhiteboardsModule).Assembly;
    private static readonly Assembly Clips = typeof(Modules.Clips.ClipsModule).Assembly;
    private static readonly Assembly BuildingBlocks = typeof(BuildingBlocks.Domain.Entity).Assembly;

    private const string InfrastructureName = "Planvexa.Infrastructure";
    private const string ApiName = "Planvexa.Api";

    private static readonly (string Name, Assembly Assembly)[] Modules =
    [
        ("Identity", Identity),
        ("Audit", Audit),
        ("Tenancy", Tenancy),
        ("WorkManagement", WorkManagement),
        ("Collaboration", Collaboration),
        ("Notifications", Notifications),
        ("TimeTracking", TimeTracking),
        ("Planning", Planning),
        ("Reporting", Reporting),
        ("Documents", Documents),
        ("Forms", Forms),
        ("Automations", Automations),
        ("Integrations", Integrations),
        ("Governance", Governance),
        ("Ai", Ai),
        ("Mobile", Mobile),
        ("Chat", Chat),
        ("Goals", Goals),
        ("Whiteboards", Whiteboards),
        ("Clips", Clips),
    ];

    public static TheoryData<string, Assembly, string[]> ForbiddenReferences()
    {
        var data = new TheoryData<string, Assembly, string[]>();
        foreach (var (name, assembly) in Modules)
        {
            // A module must not reference any other module, nor the composition/infrastructure layers.
            var forbidden = Modules
                .Where(m => m.Name != name)
                .Select(m => $"Planvexa.Modules.{m.Name}")
                .Append(InfrastructureName)
                .Append(ApiName)
                .ToArray();
            data.Add(name, assembly, forbidden);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ForbiddenReferences))]
    public void Modules_do_not_depend_on_other_modules_or_composition_layers(
        string moduleName, Assembly module, string[] forbidden)
    {
        var referenced = module.GetReferencedAssemblies().Select(a => a.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var name in forbidden)
        {
            referenced.ShouldNotContain(name, $"Module '{moduleName}' must not reference '{name}'.");
        }
    }

    [Fact]
    public void BuildingBlocks_kernel_has_no_dependency_on_modules_or_infrastructure()
    {
        var referenced = BuildingBlocks.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referenced.Any(n => n != null && n.StartsWith("Planvexa.Modules", StringComparison.Ordinal)).ShouldBeFalse();
        referenced.ShouldNotContain(InfrastructureName);
        referenced.ShouldNotContain("Planvexa.SharedContracts");
    }
}
