using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Ai.Domain;

/// <summary>
/// A workspace's AI provider configuration (one per workspace): a LiteLLM / OpenAI-compatible chat-completions
/// base URL, the model to request, and an encrypted API key. When absent, disabled, or missing a base URL
/// the host falls back to the offline deterministic provider. The key is stored encrypted and is
/// write-only over the API (reads return a masked hint).
/// </summary>
public sealed class AiProviderSettings : Entity, IWorkspaceOwned
{
    private AiProviderSettings()
    {
    }

    private AiProviderSettings(Guid id, Guid workspaceId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }

    /// <summary>Base URL of the OpenAI-compatible API (e.g. <c>http://localhost:4000</c>). No trailing slash.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public string Model { get; private set; } = string.Empty;

    /// <summary>Encrypted API key, or empty when the endpoint needs no key.</summary>
    public string ApiKeyEncrypted { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Admin-configurable allow-list of permitted model name patterns. Empty means "no
    /// restriction" (any model an admin sets via <see cref="Update"/> is accepted) — an empty allow-list is
    /// the default so upgrading an existing workspace never breaks its current configuration. Stored as a
    /// JSON string array, same "raw JSON text on the entity, parsed by the application layer" convention as
    /// Integrations' <c>ConfigJson</c>.
    /// </summary>
    public string AllowedModelsJson { get; private set; } = "[]";

    /// <summary>Redact email addresses before an outbound call to a real provider. Defaults on: the safest default.</summary>
    public bool RedactEmails { get; private set; } = true;

    /// <summary>Redact API-key-shaped tokens before an outbound call. Defaults on.</summary>
    public bool RedactApiKeys { get; private set; } = true;

    /// <summary>Redact credit-card-shaped digit runs before an outbound call. Defaults on.</summary>
    public bool RedactCreditCards { get; private set; } = true;

    /// <summary>Workspace-supplied additional regex patterns to redact, as a JSON string array.</summary>
    public string CustomRedactionPatternsJson { get; private set; } = "[]";

    public IReadOnlyList<string> AllowedModels => ParseList(AllowedModelsJson);

    public IReadOnlyList<string> CustomRedactionPatterns => ParseList(CustomRedactionPatternsJson);

    public RedactionOptions RedactionOptions => new(RedactEmails, RedactApiKeys, RedactCreditCards, CustomRedactionPatterns);

    /// <summary>True when this workspace should route AI calls to the configured provider.</summary>
    public bool IsUsable => IsEnabled && BaseUrl.Length > 0 && Model.Length > 0;

    public static AiProviderSettings CreateDefault(Guid id, Guid workspaceId, DateTimeOffset nowUtc)
        => new(id, workspaceId, nowUtc);

    /// <summary>
    /// Applies an update. <paramref name="apiKeyEncrypted"/> is null when the caller did not supply a new
    /// key, in which case the stored key is kept (write-only key semantics).
    /// </summary>
    public void Update(string baseUrl, string model, string? apiKeyEncrypted, bool isEnabled, DateTimeOffset nowUtc)
    {
        baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        model = (model ?? string.Empty).Trim();

        if (baseUrl.Length > 0
            && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ValidationAppException("The provider base URL must be an absolute http(s) URL.");
        }

        if (baseUrl.Length > 500)
        {
            throw new ValidationAppException("The provider base URL is too long.");
        }

        if (model.Length > 200)
        {
            throw new ValidationAppException("The model name is too long.");
        }

        if (isEnabled && (baseUrl.Length == 0 || model.Length == 0))
        {
            throw new ValidationAppException("A base URL and model are required before enabling the AI provider.");
        }

        EnsureModelAllowed(model);

        BaseUrl = baseUrl;
        Model = model;
        if (apiKeyEncrypted is not null)
        {
            ApiKeyEncrypted = apiKeyEncrypted;
        }

        IsEnabled = isEnabled;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Updates the model allow-list and redaction configuration. Kept separate from
    /// <see cref="Update"/> so the base-URL/model/key/enabled flow (used by the existing settings screen and
    /// "Test connection" probe) is untouched. Rejects an allow-list that would make the currently-configured
    /// <see cref="Model"/> disallowed — tighten the allow-list, then change the model, not the other way
    /// around in the same request, so the workspace is never left silently pointed at a model nobody approved.
    /// </summary>
    public void UpdateGovernance(
        IReadOnlyList<string> allowedModels, bool redactEmails, bool redactApiKeys, bool redactCreditCards,
        IReadOnlyList<string> customRedactionPatterns, DateTimeOffset nowUtc)
    {
        var normalizedModels = (allowedModels ?? [])
            .Select(m => (m ?? string.Empty).Trim())
            .Where(m => m.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedModels.Count > 0 && Model.Length > 0 && !normalizedModels.Any(p => MatchesModel(Model, p)))
        {
            throw new ValidationAppException(
                $"The currently configured model '{Model}' does not match the new allow-list. Update the model first.");
        }

        var normalizedPatterns = (customRedactionPatterns ?? [])
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .Take(20)
            .ToList();

        foreach (var pattern in normalizedPatterns)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(pattern);
            }
            catch (ArgumentException)
            {
                throw new ValidationAppException($"'{pattern}' is not a valid regular expression.");
            }
        }

        AllowedModelsJson = System.Text.Json.JsonSerializer.Serialize(normalizedModels);
        RedactEmails = redactEmails;
        RedactApiKeys = redactApiKeys;
        RedactCreditCards = redactCreditCards;
        CustomRedactionPatternsJson = System.Text.Json.JsonSerializer.Serialize(normalizedPatterns);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Throws when the allow-list is non-empty and <paramref name="model"/> matches none of its patterns.</summary>
    private void EnsureModelAllowed(string model)
    {
        var allowed = AllowedModels;
        if (allowed.Count == 0 || model.Length == 0)
        {
            return;
        }

        if (!allowed.Any(p => MatchesModel(model, p)))
        {
            throw new ValidationAppException(
                $"The model '{model}' is not on this workspace's approved model allow-list.");
        }
    }

    /// <summary>Exact match, or a trailing-<c>*</c> prefix match (e.g. <c>gpt-4*</c> matches <c>gpt-4-turbo</c>).</summary>
    private static bool MatchesModel(string model, string pattern)
        => pattern.EndsWith('*')
            ? model.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(model, pattern, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ParseList(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>A safe hint for a secret: empty when unset, otherwise bullets plus the last 4 characters.</summary>
    public static string Mask(string? apiKey)
        => string.IsNullOrEmpty(apiKey) ? string.Empty
            : apiKey.Length <= 4 ? new string('•', 3)
            : new string('•', 3) + apiKey[^4..];
}
