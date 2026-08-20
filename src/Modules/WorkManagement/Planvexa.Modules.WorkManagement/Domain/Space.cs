namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>Top-level container within a workspace. Holds folders and lists.</summary>
public sealed class Space : WorkEntity, IAggregateRoot
{
    private Space()
    {
    }

    private Space(Guid id, Guid workspaceId, string name, DateTimeOffset nowUtc, Guid createdBy)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        CreatedAtUtc = nowUtc;
        CreatedByUserId = createdBy;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Color { get; private set; }
    public string? Icon { get; private set; }

    /// <summary>The SavedView shown by default when a user opens this space (null = fall back to the first view).</summary>
    public Guid? DefaultViewId { get; private set; }

    /// <summary>This Space's status-scheme override (null = inherit the workspace default scheme).</summary>
    public Guid? StatusSchemeId { get; private set; }

    public static Space Create(
        Guid id, Guid workspaceId, string name, double position, Guid createdBy, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(workspaceId, nameof(workspaceId));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));

        return new Space(id, workspaceId, name.Trim(), nowUtc, createdBy) { Position = position };
    }

    public void Update(string? name, string? description, string? color, string? icon, Guid userId, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }

        if (description is not null)
        {
            Description = description;
        }

        if (color is not null)
        {
            Color = color;
        }

        if (icon is not null)
        {
            Icon = icon;
        }

        Touch(userId, nowUtc);
    }

    public void SetDefaultView(Guid? viewId, Guid userId, DateTimeOffset nowUtc)
    {
        DefaultViewId = viewId;
        Touch(userId, nowUtc);
    }

    /// <summary>Points this Space at its own status scheme, or back at the workspace default (null).</summary>
    public void SetStatusScheme(Guid? schemeId, Guid userId, DateTimeOffset nowUtc)
    {
        StatusSchemeId = schemeId;
        Touch(userId, nowUtc);
    }
}
