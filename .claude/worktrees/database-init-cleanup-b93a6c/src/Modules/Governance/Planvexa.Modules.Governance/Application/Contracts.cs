namespace Planvexa.Modules.Governance.Application;

// ---- DTOs ----
public sealed record SecuritySettingsDto(bool SsoEnabled, string? SamlEntityId, string? SamlMetadataUrl, bool ScimEnabled, bool ScimTokenSet, bool MfaRequired);

public sealed record AuditEntryDto(Guid Id, Guid? ActorUserId, string Action, string EntityType, Guid? EntityId, string? IpAddress, DateTimeOffset CreatedAtUtc);

public sealed record ExportJobDto(Guid Id, string Dataset, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset? CompletedAtUtc, int? RowCount);

public sealed record RetentionPolicyDto(int DeletedTaskRetentionDays, int AuditRetentionDays, bool LegalHold);

public sealed record IpAllowRuleDto(Guid Id, string Cidr, string? Description, DateTimeOffset CreatedAtUtc);

// ---- Commands ----
public sealed record UpdateSecuritySettingsCommand(bool? SsoEnabled, string? SamlEntityId, string? SamlMetadataUrl, bool? ScimEnabled, string? ScimToken, bool? MfaRequired);

public sealed record UpdateRetentionPolicyCommand(int? DeletedTaskRetentionDays, int? AuditRetentionDays, bool? LegalHold);

public sealed record AddIpAllowRuleCommand(string Cidr, string? Description);

