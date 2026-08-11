namespace Planvexa.Modules.Ai.Application;

// ---- DTOs ----
public sealed record AiSummaryDto(Guid TaskId, string Summary, int TokensEstimated);

public sealed record AiSubtasksDto(IReadOnlyList<string> Titles, int TokensEstimated);

public sealed record AiPriorityDto(string Priority, string Rationale, int TokensEstimated);

public sealed record AiUsageDto(int RequestCount, long TokensEstimated, bool CreditsEnabled, long? CreditLimit);

/// <summary>Provider settings as returned to admins. <paramref name="ApiKeyMask" /> never contains the key.</summary>
public sealed record AiProviderSettingsDto(string BaseUrl, string Model, string ApiKeyMask, bool IsEnabled, bool AiFeaturesEnabled, int? CreditLimit);

/// <summary>A null/blank <paramref name="ApiKey" /> keeps the stored key (write-only key). <paramref name="CreditLimit" />
/// is a monthly cap (calendar month, UTC) on estimated tokens spent through the real provider; null means unlimited.</summary>
public sealed record UpdateAiProviderSettingsCommand(string BaseUrl, string Model, string? ApiKey, bool IsEnabled, int? CreditLimit = null);

/// <summary>The workspace's "allow AI to be completely disabled" master switch, readable by any Member+
/// (not just Admin+) so a client can decide whether to show AI entry points at all.</summary>
public sealed record AiFeatureStatusDto(bool Enabled);

/// <summary>Result of a live probe against the configured provider.</summary>
public sealed record AiProviderTestDto(bool Ok, string Message);

/// <summary>A task's risk flag. <paramref name="AtRisk"/> mirrors <paramref name="Status"/> == "AtRisk" for convenience.</summary>
public sealed record AiRiskDto(bool AtRisk, string Status, string Reason, int TokensEstimated);

/// <summary>item 2+3: a workspace's AI governance configuration (model allow-list + redaction).</summary>
public sealed record AiGovernanceDto(
    IReadOnlyList<string> AllowedModels, bool RedactEmails, bool RedactApiKeys, bool RedactCreditCards,
    IReadOnlyList<string> CustomRedactionPatterns);

public sealed record UpdateAiGovernanceCommand(
    IReadOnlyList<string>? AllowedModels, bool RedactEmails, bool RedactApiKeys, bool RedactCreditCards,
    IReadOnlyList<string>? CustomRedactionPatterns);
