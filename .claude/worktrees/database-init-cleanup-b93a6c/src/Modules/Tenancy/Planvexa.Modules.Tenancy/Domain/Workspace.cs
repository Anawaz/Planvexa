namespace Planvexa.Modules.Tenancy.Domain;

using System.Text.RegularExpressions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.SharedContracts.IntegrationEvents;

/// <summary>
/// The single top-level business, authorization, billing, configuration and feature-entitlement
/// boundary (AGENTS.md: "There is no Organization/Tenant layer"). A Workspace's <see cref="Entity.Id"/>
/// IS the WorkspaceId that every workspace-owned entity references.
/// </summary>
public sealed partial class Workspace : Entity, IAggregateRoot, IWorkspaceOwned
{
    private Workspace()
    {
    }

    private Workspace(Guid id, string name, string slug, Guid createdByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = id;  // A workspace owns itself
        Name = name;
        Slug = slug;
        Status = WorkspaceStatus.Active;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public WorkspaceStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Workspace Create(
        Guid id, string name, string slug, Guid createdByUserId, DateTimeOffset nowUtc, bool raiseEvent = true)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));

        var workspace = new Workspace(id, name.Trim(), NormalizeSlug(slug), createdByUserId, nowUtc);
        if (raiseEvent)
        {
            workspace.Raise(new WorkspaceCreatedIntegrationEvent(id, name.Trim(), createdByUserId));
        }

        return workspace;
    }

    public void Archive() => Status = WorkspaceStatus.Archived;

    public void Restore() => Status = WorkspaceStatus.Active;

    public static string NormalizeSlug(string slug)
    {
        Guard.AgainstNullOrWhiteSpace(slug, nameof(slug));
        var candidate = slug.Trim().ToLowerInvariant();
        if (!SlugRegex().IsMatch(candidate))
        {
            throw new ArgumentException(
                "Slug must be 2-63 chars, lowercase alphanumeric and single hyphens.", nameof(slug));
        }

        return candidate;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,61}[a-z0-9]$")]
    private static partial Regex SlugRegex();
}
