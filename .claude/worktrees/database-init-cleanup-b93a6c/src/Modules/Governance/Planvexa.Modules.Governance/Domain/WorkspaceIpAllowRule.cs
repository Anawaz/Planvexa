namespace Planvexa.Modules.Governance.Domain;

using System.Net;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// One allowed CIDR range for a workspace's API access. A workspace with zero rules is
/// unrestricted — the same "empty configuration = no restriction" convention every other optional
/// workspace security feature in this codebase uses (e.g. <see cref="RetentionPolicy"/> with no set
/// retention period). Matching is done with the stdlib <see cref="System.Net.IPNetwork"/> (.NET 8+); no
/// hand-rolled CIDR parser (AGENTS.md rule 16).
/// </summary>
public sealed class WorkspaceIpAllowRule : Entity, IWorkspaceOwned
{
    private WorkspaceIpAllowRule()
    {
    }

    private WorkspaceIpAllowRule(Guid id, Guid workspaceId, string cidr, string? description, DateTimeOffset createdAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Cidr = cidr;
        Description = description;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Cidr { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static WorkspaceIpAllowRule Create(Guid id, Guid workspaceId, string cidr, string? description, DateTimeOffset nowUtc)
    {
        var normalized = (cidr ?? string.Empty).Trim();
        if (!IPNetwork.TryParse(normalized, out var network))
        {
            throw new ValidationAppException($"'{cidr}' is not a valid CIDR range (e.g. 203.0.113.0/24 or 2001:db8::/32).");
        }

        return new WorkspaceIpAllowRule(id, workspaceId, network.ToString(), string.IsNullOrWhiteSpace(description) ? null : description.Trim(), nowUtc);
    }

    /// <summary>True when <paramref name="address"/> falls inside this rule's CIDR range. IPv4-mapped
    /// IPv6 addresses (how Kestrel sometimes reports a dual-stack socket's remote address) are unwrapped
    /// first so an IPv4 rule still matches a request that arrived as ::ffff:a.b.c.d.</summary>
    public bool Matches(IPAddress address)
    {
        var candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return IPNetwork.TryParse(Cidr, out var network) && network.Contains(candidate);
    }
}
