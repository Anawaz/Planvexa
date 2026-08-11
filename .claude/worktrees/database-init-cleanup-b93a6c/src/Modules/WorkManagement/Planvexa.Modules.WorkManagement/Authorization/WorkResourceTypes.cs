namespace Planvexa.Modules.WorkManagement.Authorization;

/// <summary>
/// The resource_type strings this module owns in tenancy.resource_permissions (ADR-0003).
/// Lowercase, matching the schema. Free-form by design — see ResourcePermission's doc comment; other
/// modules define their own constants for their own resourceType values.
/// </summary>
public static class WorkResourceTypes
{
    public const string Space = "space";
    public const string Folder = "folder";
    public const string List = "list";
    public const string Task = "task";
}
