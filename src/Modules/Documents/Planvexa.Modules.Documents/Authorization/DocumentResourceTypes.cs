namespace Planvexa.Modules.Documents.Authorization;

/// <summary>
/// The resource_type string this module owns in tenancy.resource_permissions (ADR-0003), mirroring
/// WorkManagement's WorkResourceTypes. Lets a Document be shared with specific Users/Teams without a
/// dedicated DocumentShare table — grants are stored generically by Tenancy and resolved here.
/// </summary>
public static class DocumentResourceTypes
{
    public const string Document = "document";
}
